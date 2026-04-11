module WereMF.Skill.HeChong

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.Role.HeChong
open WereMF.Role.Leaf
open WereMF.Role.ShiWu

let private requestHandlerFromLeaf (random: Random) (hint: RawMessage) (leaf: LeafRole) (entity : Entity) =
    let handlers = leaf.GetQueriedHandlers random
    let handlers = handlers |> List.filter (fun h ->
            let chara = getHandlerCharaType h entity
            chara <> HeChong && chara <> Leaf
        )
    if handlers.Length = 0 then
        None
    elif handlers.Length = 1 then
        Some handlers.Head
    else
    
    let append = [1..handlers.Length] |> List.map (fun i ->
        $"{i}：{(getHandlerCharaType handlers[i-1] entity).ToString()}") |> String.concat "；"
    let msg = { hint with Content = hint.Content + "：" + append }
    let parser input = monad {
        let! int = parseInt input
        if int < 1 || int > handlers.Length then
            return! Error "未知格式"
        else
            handlers[int-1]
    }
    requestInputWithRawMessage msg ApiType.RequestHechongCopyLeaf parser |> Some

type HeChongSkill =
    | HeChongSkill
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (d: HeChongRole) -> { d with LastSelected = d.LastSelected.Add target })
                             handler
            let game = entity |> game.UpdateEntity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            let th = tEntity |> Entity.getQueriedHandler main.Rng
            let source = sending |> getSource
            let entity = source |> game.GetEntity

            if th.IsNone then
                sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.HechongSkillFailBySmogNotify
                this
            else

            let th =
                if tEntity |> getCamp <> Yezi then th else
                match tEntity.Role with
                | :? LeafRole as leaf when leaf.Fury ->
                    let msg = { Type = ToPlayer entity.Player; Content = "选择一个身份复制" }
                    requestHandlerFromLeaf main.Rng msg leaf tEntity
                | _ -> th

            if th.IsNone then
                sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.HechongSkillFailByLeafNotify
                this
            else

            let th = th.Value
            let chara = getHandlerCharaType th tEntity
            if chara = Leaf || chara = HeChong then
                sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.HechongSkillFailByInvalidCharaNotify
                this
            else

            let tEntity = tEntity |> exposeIfShiWu th
            let game = game.UpdateEntity tEntity

            let role = th.GetFromEntity tEntity
            sendRawMessage { Type = ToPlayer entity.Player; Content = role |> getSummaryName } ApiType.HechongSkillSuccessCopyNotify

            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (f: HeChongRole) -> { f with CopiedRole = Some role })
                             handler
            let game = game.UpdateEntity entity
            let sub = entity |> getFromRoleWithHandler
                            (fun (f: HeChongRole) -> f.GetSubHandler ())
                            handler
            let hs = role |> getPendingHandlers entity.Player
            let handlers = hs |> List.map (fun h -> handler.Bind ( sub.Bind h) )
            let ps = handlers |> List.map (fun h -> createPendingSkill main.Rng h entity)
            let night = { night with PendingSkills = ps @ night.PendingSkills }

            do! State.put ((main, game), night)
            this
        }

let heChongSendSkill ps (game: WereMF.State.GameContext) =
    let entity = game.GetEntity ps.Source
    let last =
        match ps.Handler.GetFromEntity entity with
        | :? HeChongRole as he -> he.LastSelected.Selected
        | _ -> []
        
    let title = "输入一名其他玩家的编号复制其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能复制自己"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> filterExceptIndexList last "不能连续模仿同一个玩家"
    let filter = giveUpOrFilterWith filter
    let def () = HeChongSkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r HeChongSkill |> Some ])
    ps |> sendSkillWith title ApiType.RequestHechongSkill filter parser def
