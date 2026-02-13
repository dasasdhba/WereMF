module WereMF.Skill.FaMao

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.FaMao
open WereMF.State

type FaMaoSkill =
    {
        Success : bool
        Healed : bool
    }
    static member New () = { Success = false ; Healed = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            
            let target = sending |> getRealTarget
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想给{recv}丢药水，被Doge挡了"
                do! State.put { context with Night = night }
                this
            else
                { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = -10
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Success |> not then None else
            
            let! context = State.get
            
            let target = sending |> getRealTarget
            let entity = context.Game.GetEntity target
            let reversed = entity.State.PotionCount >= 1
            let entity = { entity with State = entity.State |> EntityState.clearMarks }
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            
            if this.Healed then None else
            
            let recv = target |> getPlayerName context.Game
            sendMessage { Type = Public ; Content = $"{recv}被丢了药水" }
            
            if reversed then
                if entity.Role |> getCharaType = Leaf then
                    Some {
                        Target = entity
                        Request = DeadRequest.New Sudden
                    }
                else
                    sendMessage { Type = Public ; Content = $"{recv}的阵营反转了！" }
                    let entity = { entity with State.Reversed = entity.State.Reversed |> not }
                    let context = { context with Game = context.Game.UpdateEntity entity }
                    do! State.put context
                    None
            else

            let entity = { entity with State = entity.State |> EntityState.addPotion }
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            None
        }
    interface ISkillHealDeadSudden with
        member this.CanHeal () =
            this.Healed |> not
        member this.Heal target =
            sendMessage { Type = Public ; Content = $"但是{target}被救活了" }
            { this with Healed = true }

// 获取最大投掷数量（第一晚2瓶，之后1瓶）
let getFaMaoMaxCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? FaMaoRole as faMaoRole -> if faMaoRole.FirstRound then 1 else 2
    | _ -> 1

// 法猫技能发送
let faMaoSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let maxCount = getFaMaoMaxCount ps.Handler entity
    
    let title = $"输入要投掷药水的玩家编号（最多 {maxCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = maxCount
        MaxCountError = Some $"最多投掷 {maxCount} 瓶药水"
        DuplicateError = Some "不能重复投掷同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能给自己丢药水"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (FaMaoSkill.New ()) :> ISkill
    
    let createSkill id = Skill.New ps id (FaMaoSkill.New ())
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser def
