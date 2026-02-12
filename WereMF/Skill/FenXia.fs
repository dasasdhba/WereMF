module WereMF.Skill.FenXia

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
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
            sending |> getRealTarget |> context.Game.HasEntity
    interface ISkillCost with
        member this.Cost sending = monad {
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            // 双弹簧默认 -1
            let target = if sending.Spring.IsSome then source else sending.Target
            let tEntity = target |> context.Game.GetEntity
            let cost = if tEntity.State |> EntityState.isDead then 2 else 1
            let entity = entity |> updateRoleWithHandler
                             (fun (f: FenXiaRole) -> { f with FenCount = f.FenCount - cost })
                             handler
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let tEntity = target |> context.Game.GetEntity
            let remain = entity |> getFromRoleWithHandler
                            (fun f -> f.FenCount)
                            handler
            let remain = defaultArg remain 0
            let skill, context =
                if remain > 0 then this, context else
                sendMessage { Type = ToPlayer entity.Player ; Content = "你的粉条用完了" }
                let state = context.Night.GetPlayerState source
                let state = { state with Blocked = true }
                let context = { context with Night = context.Night.SetPlayerState state }
                { this with Dead = true }, context
            do! State.put context
            if target |> isDoged context.Night then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想给{recv}发粉条，被Doge挡了"
                do! State.put { context with Night = night }
                skill
            else
                if tEntity.State |> EntityState.isDead && tEntity.State.Dead.Name = "???" then
                    sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                    skill
                else
                
                let h = tEntity |> Entity.getQueriedHandler context.Main.Rng
                
                if h.IsNone then
                    sendMessage { Type = ToPlayer entity.Player; Content = "失败" }
                    skill
                else
                
                // 实物
                let h = h.Value
                let tEntity = tEntity |> exposeIfShiWu h
                let context = { context with Game = context.Game.UpdateEntity tEntity }
                do! State.put context

                let chara = getHandlerCharaType h tEntity
                if chara = FenXia || chara = Leaf then
                    sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                    skill
                else
                
                sendMessage { Type = ToPlayer entity.Player ; Content = chara.ToString () }
                let role = createRole context.Main.Roll chara
                let entity = entity |> updateRoleWithHandler
                                 (fun (f: FenXiaRole) -> { f with CopiedRoles = role :: f.CopiedRoles })
                                 handler
                let context = { context with Game = context.Game.UpdateEntity entity }
                let sub = createSubFunctor
                           (fun k -> k.CopiedRoles[0])
                           (fun v k ->
                     { k with CopiedRoles = k.CopiedRoles |> List.updateAt 0 v })
                let hs = role |> getPendingHandlers entity.Player
                let handlers = hs |> List.map (fun h -> handler.Bind ( (sub |> CommonHandler).Bind h))
                let ps = handlers |> List.map (fun h -> createPendingSkill h entity)
                let context = { context with Night.PendingSkills = ps @ context.Night.PendingSkills }
                do! State.put context
                skill
        }
    interface ISkillSummary with
        member this.Priority = 9
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.Dead |> not then None else
            
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
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
        let cost = if isDead then 2 else 1
        
        if cost > fenCount then
            if isDead then
                Error "你的粉条不足（需要 2 根）"
            else
                Error "你的粉条不足（需要 1 根）"
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
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterFenXia game fenCount
                >> (if fenCount <= 0 then filterDisabled "你没有粉条了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = (FenXiaSkill.New ()) :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (FenXiaSkill.New ()) |> Some ])
    
    ps |> sendSkillWith title filter parser def
