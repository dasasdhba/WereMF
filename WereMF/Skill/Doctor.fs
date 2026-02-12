module WereMF.Skill.Doctor

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.Doctor

type DoctorSkill =
    {
        Success : bool
        Healed : bool
    }
    static member New () = { Success = false ; Healed = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (d: DoctorRole) -> { d with Capsule = d.Capsule - 1 })
                             handler
            let context = { context with Game = context.Game.UpdateEntity entity }
            
            let target = sending |> getRealTarget
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想给{recv}扎针，被 doge 挡了"
                do! State.put { context with Night = night }
                this
            else
                do! State.put context
                { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = -5
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Healed || this.Success |> not then None else
            
            let! context = State.get
            
            let target = sending |> getRealTarget
            let entity = context.Game.GetEntity target
            let recv = target |> getPlayerName context.Game
            sendMessage { Type = Public ; Content = $"{recv}被扎了一针" }
            
            let entity = { entity with State = entity.State |> EntityState.addCapsule }
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            
            if entity.State.CapsuleCount < 2 then None else
            
            Some {
                Target = entity
                Request = DeadRequest.New Sudden
            }
        }
    interface ISkillHealDeadKill with
        member this.CanHeal () =
            this.Healed |> not
        member this.Heal target =
            sendMessage { Type = Public ; Content = $"但是{target}被救活了" }
            { this with Healed = true }
            

// 获取剩余药丸数量
let getCapsuleCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? DoctorRole as doctorRole -> doctorRole.Capsule
    | _ -> 0

// 庸医技能发送
let doctorSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let capsuleCount = getCapsuleCount ps.Handler entity
    
    let title = $"输入要扎针的玩家编号（最多 {capsuleCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = capsuleCount
        MaxCountError = Some $"药丸数量不足，你只有 {capsuleCount} 个药丸"
        DuplicateError = Some "不能重复扎同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if capsuleCount <= 0 then filterDisabled "你没有药丸了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = DoctorSkill.New () :> ISkill
    let createSkill id = Skill.New ps id (DoctorSkill.New ())
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser def
