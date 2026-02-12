module WereMF.Skill.Mole

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.Mole

let private roll : int list = [ 0; 0; 1; 1; 1; 2 ]

type MoleSkill =
    {
        Success : bool
        Dead : bool
    }
    static member New () = { Success = false ; Dead = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let player = entity.Player
            
            let red = entity |> getFromRoleWithHandler
                        (fun m -> m.RedGround)
                        handler
            let red = defaultArg red false
            
            let r = roll |> List.randomChoiceWith context.Main.Rng
            let context, skill, success =
                match r with
                | 1 ->
                    sendMessage { Type = ToPlayer player; Content = "成功" }
                    context, this, true
                | 2 when red |> not ->
                    let entity = entity |> updateRoleWithHandler
                                    (fun m -> { m with RedGround = true })
                                    handler
                    let context = { context with Game = context.Game.UpdateEntity entity }
                    let msg = { Type = ToPlayer player; Content = "红土地，要再突击一次吗？（1：突击；0：放弃）" }
                    let yes = requestInputWithMessage msg parseBool
                    if yes then
                        let ps = createPendingSkill handler entity
                        let context = { context with Night.PendingSkills = ps :: context.Night.PendingSkills }
                        context, this, true
                    else
                        context, this, true
                | 2 when red ->
                    sendMessage { Type = ToPlayer player; Content = "红土地，你死了" }
                    context, { this with Dead = true }, false
                | _ ->
                    sendMessage { Type = ToPlayer player; Content = "失败" }
                    context, this, false
            
            if success |> not then
                do! State.put context
                skill
            else
            
            let target = sending |> getRealTarget
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想突击{recv}，被doge挡了"
                do! State.put { context with Night = night }
                skill
            else
                do! State.put context
                { skill with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = 0
        member this.GetRealTarget sending =
            if this.Dead then sending |> getSource
            else sending |> getRealTarget
        member this.Summarize sending = monad {
            let! context = State.get
            
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let sender = sending |> getSenderName context.Game
            
            if this.Dead then
                sendMessage { Type = Public ; Content = $"{sender}两次冲到了红土地上！" }
                Some {
                    Target = entity
                    Request = DeadRequest.FromSelf sender Kill
                }
            else
            
            if this.Success |> not then None else
            
            let recv = sending.Target |> getPlayerName context.Game
            let target = sending |> getRealTarget
            let tEntity = target |> context.Game.GetEntity
            if sending.Spring.IsNone then
                sendMessage { Type = Public; Content = $"{recv}被{sender}突击了！" }
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Kill
                }
            else
                sendMessage { Type = Public; Content = $"{sender}想突击{recv}，被弹簧弹回！" }
                Some {
                    Target = tEntity
                    Request = DeadRequest.FromSelf sender Kill
                }
        }

let moleSendSkill ps game =
    let title = "输入一名玩家的编号进行突击，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能突击自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (MoleSkill.New ()) :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (MoleSkill.New ()) |> Some ])
    ps |> sendSkillWith title filter parser def
