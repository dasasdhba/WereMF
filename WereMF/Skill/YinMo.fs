module WereMF.Skill.YinMo

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.YinMo

type YinMoSkill =
    {
        Success : PlayerId option
    }
    static member New () = { Success = None }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get

            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (y: YinMoRole) -> { y with DiscCount = y.DiscCount - 1 })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get

            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                sendRawMessage { Type = ToPlayer (sending |> getSource |> game.GetEntity).Player ; Content = "失败" } "yinmo_skill_fail_by_doge_notify"
                let night = night.AddMessage $"{sender}想暴毙{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                { this with Success = Some target }
        }
    interface ISkillExecuteQueued with
        member this.Execute sending = monad {
            if this.Success.IsNone then this else
            let target = this.Success.Value
            let! (main, game), night = State.get
            let state = night.GetPlayerState target
            let state = { state with Blocked = true }
            let night = night.SetPlayerState state
            do! State.put ((main, game), night)
            this
        }
    interface ISkillSummary with
        member this.Priority = 1
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Success.IsNone then None else
            let! (main, game), night = State.get

            let sender = sending |> getSenderName game
            let recv = sending.Target |> getPlayerName game
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            let camp = tEntity |> Entity.getCamp
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let entity =
                if camp = Boom then entity else
                let handler = sending |> getHandler
                entity |> updateRoleWithHandler
                         (fun (y: YinMoRole) -> { y with Disabled = Some true })
                         handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)

            if sending.Spring.IsNone then
                sendRawMessage { Type = Public; Content = $"{sender}给{recv}发了唱片！" } "yinmo_kill_broadcast"
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Sudden
                }
            else
                sendRawMessage { Type = Public; Content = $"{sender}想暴毙{recv}，被弹簧弹回！" } "yinmo_kill_spring_broadcast"
                Some {
                    Target = tEntity
                    Request = DeadRequest.FromSelf sender Sudden
                }
        }

// 音魔技能发送
let yinMoSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let disc, isDisabled = 
        match ps.Handler.GetFromEntity entity with
        | :? YinMoRole as yinMo -> yinMo.DiscCount, yinMo.IsDisabled()
        | _ -> 0, false
    
    let title = $"输入要发唱片的玩家编号（剩余 {disc} 张唱片），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable ps.Source game
                >> filterExceptIndex ps.Source "你不能给自己发唱片"
                >> filterKidnapped ps
                >> (if isDisabled then filterDisabled "你的技能在冷却" else id)
    let filter = giveUpOrFilterWith filter
    let def () = (YinMoSkill.New ()) :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (YinMoSkill.New ())  |> Some ])
    
    ps |> sendSkillWith title "request_yinmo_skill" filter parser def
