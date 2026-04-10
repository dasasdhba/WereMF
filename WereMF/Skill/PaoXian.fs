module WereMF.Skill.PaoXian

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Skill
open WereMF.Module.Cli

type PaoXianSkill =
    {
        Success : bool
    }
    static member New () = { Success = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get

            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                sendRawMessage { Type = ToPlayer (sending |> getSource |> game.GetEntity).Player ; Content = "失败" } "paoxian_skill_fail_by_doge_notify"
                let night = night.AddMessage $"{sender}想杀{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = 0
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Success |> not then None else
            let! (main, game), night = State.get

            let sender = sending |> getSenderName game
            let recv = sending.Target |> getPlayerName game
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            if sending.Spring.IsNone then
                sendRawMessage { Type = Public; Content = $"{recv}被{sender}杀了" } "paoxian_kill_broadcast"
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Kill
                }
            else
                sendRawMessage { Type = Public; Content = $"{sender}想杀{recv}，被弹簧弹回！" } "paoxian_kill_spring_broadcast"
                Some {
                    Target = tEntity
                    Request = DeadRequest.FromSelf sender Kill
                }
        }

let paoXianSendSkill ps game =
    let title = "输入一名玩家的编号令其死亡，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能杀死自己"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (PaoXianSkill.New ()) :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (PaoXianSkill.New ()) |> Some ])
    ps |> sendSkillWith title "request_paoxian_skill" filter parser def
