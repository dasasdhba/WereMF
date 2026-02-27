module WereMF.Skill.Kirby

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
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
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                this
            else

            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                let night = night.AddMessage $"{sender}想吸入{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                let handler = sending |> getHandler
                let state = night.GetPlayerState target
                let state = { state with Kirby = Some (source, handler) }
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
            | Some (s, _) when s = source ->
                let th = tEntity.Role |> getQueriedHandler main.Rng
                let chara = tEntity |> getHandlerCharaType th
                if chara = Kirby || chara = Leaf then
                    sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                    None
                else
                    sendMessage { Type = ToPlayer entity.Player ; Content = chara.ToString () }
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
    ps |> sendSkillWith title filter parser def
