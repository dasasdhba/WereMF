module WereMF.Skill.Kirby

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.Role.Bind
open WereMF.Role.Kirby

type KirbySkill =
    | KirbySkill
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (k: KirbyRole) -> { k with CopiedRole = None })
                             handler
            let game = entity |> game.UpdateEntity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            if sending.Spring.IsSome then
                sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.KirbySkillFailBySpringNotify
                this
            else

            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.KirbySkillFailByDogeNotify
                let night = night.AddMessage $"{sender}想吸入{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                let handler = sending |> getHandler
                let state = night.GetPlayerState target
                let state = { state with Kirby = Some sending.Pending }
                let night = night.SetPlayerState state
                do! State.put ((main, game), night)
                this
        }
    interface ISkillSummary with
        member this.Priority = 10
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if sending.Spring.IsSome then None else

            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            let state = night.GetPlayerState target
            let kirby = state.Kirby
            match kirby with
            | Some pending when pending.Id = sending.Pending.Id ->
                let th = tEntity.Role |> getQueriedHandler main.Rng
                let chara = tEntity |> getHandlerCharaType th
                if chara = Kirby || chara = Leaf then
                    sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.KirbySkillFailByInvalidCharaNotify
                    None
                else
                    sendRawMessage { Type = ToPlayer entity.Player ; Content = chara.ToString () } ApiType.KirbySkillSuccessCharaNotify
                    let role = chara |> createRole main.Roll
                    let handler = sending |> getHandler
                    let entity = entity |> updateRoleWithHandler
                                     (fun (k: KirbyRole) -> { k with CopiedRole = Some role })
                                     handler
                    let game = game.UpdateEntity entity
                    do! State.put ((main, game), night)
                    None
            | _ -> None
        }

let kirbySendSkill ps game =
    let title = "输入一名玩家的编号吸入，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能吸入自己"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = KirbySkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r KirbySkill |> Some ])
    ps |> sendSkillWith title ApiType.RequestKirbySkill filter parser def
