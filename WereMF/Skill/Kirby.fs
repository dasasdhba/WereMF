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
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            if sending.Spring.IsSome then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                this
            else
            
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName context.Game
            let recv = target |> getPlayerName context.Game
            if target |> isDoged context.Night then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                let night = context.Night.AddMessage $"{sender}想吸入{recv}，被 doge 挡了"
                do! State.put { context with Night = night }
                this
            else
                let handler = sending |> getHandler
                let state = context.Night.GetPlayerState target
                let state = { state with Kirby = Some (source, handler) }
                let night = context.Night.SetPlayerState state
                do! State.put { context with Night = night }
                this
        }
    interface ISkillSummary with
        member this.Priority = 10
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if sending.Spring.IsSome then None else
            
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let target = sending |> getRealTarget
            let tEntity = target |> context.Game.GetEntity
            let state = context.Night.GetPlayerState target
            let kirby = state.Kirby
            match kirby with
            | Some (s, _) when s = source ->
                let th = tEntity.Role |> getValidHandlers |> List.randomChoiceWith context.Main.Rng
                let chara = tEntity |> getHandlerCharaType th
                if chara = Kirby || chara = Leaf then
                    sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                    None
                else
                    sendMessage { Type = ToPlayer entity.Player ; Content = chara.ToString () }
                    let role = chara |> createRole context.Main.Roll
                    let handler = sending |> getHandler
                    let entity = entity |> updateRoleWithHandler
                                     (fun (k: KirbyRole) -> { k with CopiedRole = Some role })
                                     handler
                    do! State.put { context with Game = context.Game.UpdateEntity entity }
                    None
            | _ -> None
        }

let kirbySendSkill ps game =
    let title = "输入一名玩家的编号吸入，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能吸入自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = KirbySkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r KirbySkill |> Some ])
    ps |> sendSkillWith title filter parser def
