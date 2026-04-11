module WereMF.Skill.FaMao

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
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
            let! (main, game), night = State.get

            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想给{recv}丢药水，被Doge挡了"
                do! State.put ((main, game), night)
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

            let! (main, game), night = State.get

            let target = sending |> getRealTarget
            let entity = game.GetEntity target
            let reversed = entity.State.PotionCount >= 1
            let entity = { entity with State = entity.State |> EntityState.clearMarks }
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)

            if this.Healed then None else

            let recv = target |> getPlayerName game
            sendRawMessage { Type = Public ; Content = $"{recv}被丢了药水" } ApiType.FamaoSkillBroadcast

            if reversed then
                if target = (sending |> getSource) then
                    sendRawMessage { Type = Public ; Content = "但是什么也没有发生" } ApiType.FamaoReverseFailedBroadcast
                    None
                else
                    sendRawMessage { Type = Public ; Content = $"{recv}的阵营反转了！" } ApiType.FamaoReversedBroadcast
                    let entity = { entity with State.Reversed = entity.State.Reversed |> not }
                    let game = game.UpdateEntity entity
                    do! State.put ((main, game), night)
                    None
            else

            let entity = { entity with State = entity.State |> EntityState.addPotion }
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            None
        }
    interface ISkillHealDeadSudden with
        member this.CanHeal () =
            this.Healed |> not
        member this.Heal target =
            sendRawMessage { Type = Public ; Content = $"但是{target}被救活了" } ApiType.FamaoSaveBroadcast
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
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (FaMaoSkill.New ()) :> ISkill
    
    let createSkill id = Skill.New ps id (FaMaoSkill.New ())
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title ApiType.RequestFamaoSkill filter parser def
