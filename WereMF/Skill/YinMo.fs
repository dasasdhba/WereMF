module WereMF.Skill.YinMo

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.YinMo

type YinMoSkill =
    {
        Success : bool
    }
    static member New () = { Success = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
           
            let target = sending |> getRealTarget
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想暴毙{recv}，被Doge挡了"
                do! State.put { context with Night = night }
                this
            else
                let night = context.Night
                let state = night.GetPlayerState target
                let state = { state with Blocked = true }
                let night = night.SetPlayerState state
                let context = { context with Night = night }
                do! State.put context
                { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = 1
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Success |> not then None else
            let! context = State.get
            
            let sender = sending |> getSenderName context.Game
            let recv = sending.Target |> getPlayerName context.Game
            let target = sending |> getRealTarget
            let tEntity = target |> context.Game.GetEntity
            let camp = tEntity |> Entity.getCamp
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let entity =
                if camp = Boom then entity else
                let handler = sending |> getHandler
                entity |> updateRoleWithHandler
                         (fun (y: YinMoRole) -> { y with Disabled = Some true })
                         handler
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            
            if sending.Spring.IsNone then
                sendMessage { Type = Public; Content = $"{sender}给{recv}发了唱片！" }
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Sudden
                }
            else
                sendMessage { Type = Public; Content = $"{sender}想暴毙{recv}，被弹簧弹回！" }
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
                >> filterSelectable game
                >> filterExceptIndex ps.Source "你不能给自己发唱片"
                >> filterKidnapped ps
                >> (if isDisabled then filterDisabled "你的技能在冷却" else id)
    let filter = giveUpOrFilterWith filter
    let def () = (YinMoSkill.New ()) :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (YinMoSkill.New ())  |> Some ])
    
    ps |> sendSkillWith title filter parser def
