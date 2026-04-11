module WereMF.Skill.FenXia

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.Role.Bind
open WereMF.Role.ShiWu
open WereMF.State
open WereMF.Role.FenXia

type FenXiaSkill =
    {
        Dead : bool
    }
    static member New () = { Dead = false }
    interface ISkill
    interface ISkillCanExecute with
        member this.CanExecute context sending =
            let (main, game), night = context
            sending |> getRealTarget |> game.HasEntity
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = if sending.Spring.IsSome then source else sending.Target
            let tEntity = target |> game.GetEntity
            let cost = if tEntity.State |> EntityState.isDead then 1 else 2
            let entity = entity |> updateRoleWithHandler
                             (fun (f: FenXiaRole) -> { f with FenCount = f.FenCount - cost })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let remain = entity |> getFromRoleWithHandler
                            (fun f -> f.FenCount)
                            handler
            let skill, night =
                if remain > 0 then this, night else
                sendRawMessage { Type = ToPlayer entity.Player ; Content = "你的粉条用完了" } ApiType.FenxiaSkillNoFenNotify
                let state = night.GetPlayerState source
                let state = { state with Blocked = true }
                let night = night.SetPlayerState state
                { this with Dead = true }, night
            do! State.put ((main, game), night)
            if target |> isDoged night then
                sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.FenxiaSkillFailedByDogeNotify
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想给{recv}发粉条，被Doge挡了"
                do! State.put ((main, game), night)
                skill
            else
                let tEntity = target |> game.GetEntity
                if tEntity.State |> EntityState.isDead && tEntity.State.Dead.Name = "???" then
                    sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.FenxiaSkillFailedByUnknownDeadNotify
                    skill
                else

                let h = tEntity |> Entity.getQueriedHandler main.Rng

                if h.IsNone then
                    sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.FenxiaSkillFailedBySmogNotify
                    skill
                else

                let h = h.Value
                let tEntity = tEntity |> exposeIfShiWu h
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)

                let chara = getHandlerCharaType h tEntity
                if chara = FenXia || chara = Leaf then
                    sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } ApiType.FenxiaSkillFailedByInvalidCharaNotify
                    skill
                else

                sendRawMessage { Type = ToPlayer entity.Player ; Content = chara.ToString () } ApiType.FenxiaSkillSuccessCharaNotify
                let role = createRole main.Roll chara
                let entity = source |> game.GetEntity
                let entity = entity |> updateRoleWithHandler
                                 (fun (f: FenXiaRole) -> { f with CopiedRoles = f.CopiedRoles @ [role] })
                                 handler
                let game = game.UpdateEntity entity
                let sub = entity |> getFromRoleWithHandler
                            (fun (f: FenXiaRole) -> f.GetSubHandler (f.CopiedRoles.Length-1))
                            handler
                let hs = role |> getPendingHandlers entity.Player
                let handlers = hs |> List.map (fun h -> handler.Bind (sub.Bind h))
                let ps = handlers |> List.map (fun h -> createPendingSkill main.Rng h entity)
                let night = { night with PendingSkills = ps @ night.PendingSkills }
                do! State.put ((main, game), night)
                skill
        }
    interface ISkillSummary with
        member this.Priority = 8
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.Dead |> not then None else

            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            Some {
                Target = entity
                Request = DeadRequest.New Force
            }
        }

// 过滤：允许选择任何玩家，但根据生死状态决定粉条消耗
let filterFenXia (game: GameContext) (fenCount: int) = function
    | Ok playerId ->
        let entity = game.GetEntity playerId
        let isDead = entity.State |> EntityState.isDead
        let cost = if isDead then 1 else 2
        
        if cost > fenCount then
            if isDead then
                Error "你的粉条不足（需要 1 根）"
            else
                Error "你的粉条不足（需要 2 根）"
        else
            Ok playerId
    | value -> value

// 粉侠技能发送
let fenXiaSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let fenCount = 
        match ps.Handler.GetFromEntity entity with
        | :? FenXiaRole as fenXia -> fenXia.FenCount
        | _ -> 0
    
    let title = $"输入要获取技能的角色编号（剩余 {fenCount} 根粉条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterExceptIndex ps.Source "你不能给自己粉条"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> filterFenXia game fenCount
                >> (if fenCount <= 0 then filterDisabled "你没有粉条了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = (FenXiaSkill.New ()) :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (FenXiaSkill.New ()) |> Some ])
    
    ps |> sendSkillWith title ApiType.RequestFenxiaSkill filter parser def
