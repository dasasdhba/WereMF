module WereMF.Skill.PaoXian

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
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
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
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
                sendMessage { Type = Public; Content = $"{recv}被{sender}杀了" }
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Kill
                }
            else
                sendMessage { Type = Public; Content = $"{sender}想杀{recv}，被弹簧弹回！" }
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
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (PaoXianSkill.New ()) :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (PaoXianSkill.New ()) |> Some ])
    ps |> sendSkillWith title filter parser def
