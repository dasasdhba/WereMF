module WereMF.Skill.Mole

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.Mole

type MoleSkill =
    {
        Success : bool
        Dead : bool
    }
    static member New () = { Success = false ; Dead = false }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let player = entity.Player
            
            let roll, red =
                entity |> getFromRoleWithHandler
                    (fun m -> m.Roll, m.RedGround)
                    handler
            
            let i, r = roll |> List.indexed |> List.randomChoiceWith main.Rng
            let game, night, skill, success =
                match r with
                | 1 ->
                    sendRawMessage { Type = ToPlayer player; Content = "成功" } "mole_skill_success_notify"
                    game, night, this, true
                | 2 when red |> not ->
                    let entity = entity |> updateRoleWithHandler
                                    (fun m -> { m with RedGround = true })
                                    handler
                    let game = game.UpdateEntity entity
                    let msg = { Type = ToPlayer player; Content = "红土地，要再突击一次吗？（1：突击；0：放弃）" }
                    let yes = requestInputWithRawMessage msg "request_mole_red_ground" parseBool
                    if yes then
                        let ps = createPendingSkill handler entity
                        let night = { night with PendingSkills = ps :: night.PendingSkills }
                        game, night, this, true
                    else
                        game, night, this, true
                | 2 when red ->
                    sendRawMessage { Type = ToPlayer player; Content = "红土地，你死了" } "mole_red_twice_notify"
                    game, night, { this with Dead = true }, false
                | _ ->
                    let roll = roll |> List.updateAt i 1
                    let entity = entity |> updateRoleWithHandler
                                    (fun m -> { m with Roll = roll })
                                    handler
                    let game = game.UpdateEntity entity
                    sendRawMessage { Type = ToPlayer player; Content = "失败" } "mole_skill_fail_notify"
                    game, night, this, false
            
            if success |> not then
                do! State.put ((main, game), night)
                skill
            else
            
            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想突击{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                skill
            else
                do! State.put ((main, game), night)
                { skill with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = 0
        member this.GetRealTarget sending =
            if this.Dead then sending |> getSource
            else sending |> getRealTarget
        member this.Summarize sending = monad {
            let! (main, game), night = State.get
            
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let sender = sending |> getSenderName game
            
            if this.Dead then
                sendRawMessage { Type = Public ; Content = $"{sender}两次冲到了红土地上！" } "mole_red_twice_broadcast"
                Some {
                    Target = entity
                    Request = DeadRequest.FromSelf sender Kill
                }
            else
            
            if this.Success |> not then None else
            
            let recv = sending.Target |> getPlayerName game
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            if sending.Spring.IsNone then
                sendRawMessage { Type = Public; Content = $"{recv}被{sender}突击了！" } "mole_kill_broadcast"
                Some {
                    Target = tEntity
                    Request = DeadRequest.New Kill
                }
            else
                sendRawMessage { Type = Public; Content = $"{sender}想突击{recv}，被弹簧弹回！" } "mole_kill_spring_broadcast"
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
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (MoleSkill.New ()) :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (MoleSkill.New ()) |> Some ])
    ps |> sendSkillWith title "request_mole_skill" filter parser def
