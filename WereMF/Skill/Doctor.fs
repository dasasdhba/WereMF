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
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (d: DoctorRole) -> { d with Capsule = d.Capsule - 1 })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            
            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想给{recv}扎针，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = -5
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Healed || this.Success |> not then None else
            
            let! (main, game), night = State.get
            
            let target = sending |> getRealTarget
            let entity = game.GetEntity target
            let recv = target |> getPlayerName game
            sendRawMessage { Type = Public ; Content = $"{recv}被扎了一针" } "doctor_skill_broadcast"
            
            let entity = { entity with State = entity.State |> EntityState.addCapsule }
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            
            if entity.State.CapsuleCount < 2 then None else
            
            let entity = { entity with State.Capsule = [] }
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            
            Some {
                Target = entity
                Request = DeadRequest.New Sudden
            }
        }
    interface ISkillHealDeadKill with
        member this.CanHeal () =
            this.Healed |> not
        member this.Heal target =
            sendRawMessage { Type = Public ; Content = $"但是{target}被救活了" } "doctor_save_broadcast"
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
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> (if capsuleCount <= 0 then filterDisabled "你没有药丸了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = DoctorSkill.New () :> ISkill
    let createSkill id = Skill.New ps id (DoctorSkill.New ())
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title "request_doctor_skill" filter parser def
