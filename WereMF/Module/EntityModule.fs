namespace WereMF.Module

open System
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Role
open WereMF.State

module EntityState =
    
    // property helper
    
    let isBarLeader state =
        state.BarLeader.IsSome
    let setBarLeader state =
        { state with BarLeader = Some true }
    let hasBarVote state =
        state.BarLeader = Some true
    let clearBarVote state =
        if hasBarVote state then { state with BarLeader = Some false }
        else state
    
    let isDead state =
        state.Dead.Dead
    let setDeadFlag value (state : EntityState) =
        { state with Dead.Dead = value }
    let setDeadName value (state : EntityState) =
        { state with Dead.Name = value }

    let addSmogRound round state =
        { state with Smog = round :: state.Smog }
    let addSmog state =
        state |> addSmogRound 2
        
    let addCapsuleRound round state =
        { state with Capsule = round :: state.Capsule }
    let addCapsule state =
        state |> addCapsuleRound 2

    let addPotionRound round state =
        { state with Potion = round :: state.Potion }
    let addPotion state =
        state |> addPotionRound 2
        
    let addXianSongRound round state =
        { state with XianSong = round :: state.XianSong }
    let addXianSong state =
        state |> addXianSongRound 1
        
    let isThreatenDead (state: EntityState) =
        state.Threaten.IsSome && state.Threaten.Value.Type = QueuedDeath
        
    // in game marks
        
    let clearMarks entity =
        let entity = { entity with Smog = [] }
        let entity = { entity with Bug = None }
        let entity = { entity with Capsule = [] }
        let entity = { entity with Potion = [] }
        let entity = { entity with XianSong = [] }
        entity
        
    let getTopMark entity =
        let voteBlock = if entity.JiaoHuaVoteBlocked then "\u2716" else ""
        let protect = if entity.JiaoHuaProtected then "\U0001F6E1" else ""
        let roleBlock = if entity.JiaoHuaBlocked > 0 then "\u274c" else ""
        let leafBlock = if entity.LeafProtected.IsSome then "\u274e" else ""
        voteBlock + protect + roleBlock + leafBlock
        
    let getBuffMark (entity : EntityState)=
        let repeat n s =
            if n <= 0 then ""
            elif n = 1 then s
            else [1..n] |> List.map (fun i -> s) |> String.concat ""
        let smog = repeat entity.SmogCount "\u2601"
        let bug = repeat entity.BugCount "\U0001F41E"
        let xian = repeat entity.XianSongCount "\U0001F36A"
        let cap = repeat entity.CapsuleCount "\U0001F48A"
        let drop = repeat entity.PotionCount "\U0001F4A7"
        smog + bug + xian + cap + drop
        
    // in game judge
    
    let canBeSelected state =
        not (state.JiaoHuaProtected || state.LeafProtected.IsSome
             || state.Smog |> List.exists (fun i -> i > 1))
    let canBeSelectedWithSmog state =
        not (state.JiaoHuaProtected || state.LeafProtected.IsSome)
    let canBeVoted state =
        not state.LeafProtected.IsSome
    let canVote state =
        not state.JiaoHuaVoteBlocked
    
    // in game update
        
    let updateOnNightStart entity =
        {
             entity with
                LeafProtected = updateNightOptionBool entity.LeafProtected
                Kidnapped = []
                Threaten = None
                XianSong = entity.XianSong |> List.map (fun i -> i - 1)
                Bomb = 0
                JiaoHuaVoteBlocked = false
        }
        
    let updateOnDayStart entity =
        let removeRes l =
            l |> List.map (fun i -> i - 1) |> List.filter (fun i -> i > 0)
        {
             entity with
                LeafProtected = updateNightOptionBool entity.LeafProtected
                Smog = entity.Smog |> removeRes
                Capsule = entity.Capsule |> removeRes
                Potion = entity.Potion |> removeRes
                Milk = entity.Milk.UpdateOnDayStart ()
                JiaoHuaBlocked = 0
        }
        
    let updateOnDead state=
        {
            EntityState.New() with
                Dead = state.Dead
                BarLeader = state.BarLeader
                PaoXianParty = state.PaoXianParty
                Reversed = state.Reversed
        }
    
module Entity =
    
    let getState (entity : Entity) =
        entity.State
        
    let getCharaType (entity : Entity) =
        entity.Role |> Role.getCharaType
        
    let getCamp (entity : Entity)=
        let camp = (entity.Role |> Role.getCharaType).GetCamp ()
        if not entity.State.Reversed then camp
        else camp.Reverse()
    
    let getQueriedHandler (rng :Random) (entity : Entity) =
        if entity.State.SmogCount > 0 then None
        else Some (entity.Role |> Role.getQueriedHandler rng)
        
    let getQueriedCharaType handler (entity: Entity) =
        entity.Role |> Role.getQueriedCharaType handler
        
    let getQueriedName handler (entity: Entity) =
        entity.Role |> Role.getQueriedName handler
        
    let getInGameName entity =
        let reversed = if entity.State.Reversed then "反·" else ""
        let leaf =
            match entity.Role with
            | :? IRoleLeaf as leaf when leaf.Fury -> "（叶子）"
            | _ -> ""
        let reborn = match entity.State.Reborn with
                     | Some _ -> "（复活）"
                     | None -> ""
        reversed + entity.Player.Name + leaf + reborn
        
    let getDeadName handler (entity: Entity) =
        match handler with
        | Some v -> entity |> getQueriedName v
        | None -> "???"
        
    let getSummaryName entity =
        let reversed = if entity.State.Reversed then "反·" else ""
        let barLeader = if entity.State |> EntityState.isBarLeader then "（吧主）" else ""
        reversed + (entity.Role |> Role.getSummaryName) + barLeader
        
    let getNightSummary entity =
        if entity.State |> EntityState.isDead then
            $"{entity.Player.Id.ToCircleString()}【{entity.State.Dead.Name}】"
        else
            $"{entity.Player.Id.ToCircleString()} {getInGameName entity} " +
            $"{entity.State |> EntityState.getTopMark} {entity.State |> EntityState.getBuffMark}"

    let getDaySummary entity =
        if entity.State |> EntityState.isDead then
            $"{entity.Player.Id.ToCircleString()} 【{entity.State.Dead.Name}】"
        else
            $"{entity.Player.Id.ToCircleString()} {getInGameName entity} " +
            $"{entity.State |> EntityState.getTopMark}"
        
    let getSummary (entity: Entity) =
        entity.Player.ToInGameString () + ": " + getSummaryName entity
        
    // dead check
    
    let updateOnDead dead entity =
        {
            entity with
                State = entity.State |> EntityState.updateOnDead
                Role = entity.Role |> Role.updateOnDead dead
        }
    
    let requestDead (request: DeadRequest) (context: RoleContext) entity =
        let header = request.GetName entity
        let reason = match request.DeadType with
                     | Kill -> "死了"
                     | Sudden -> "暴毙了"
                     | Force -> "暴毙了"
                     | Vote -> "出局"
        sendMessage { Type = Public ; Content = $"{header}{reason}" }
        let result = entity.Role |> tryPreventDead context request.DeadType entity
        match result with
        | Some result ->
            sendMessage { Type = Public ; Content = $"但是{header}复活了" }
            let entity = { result.NewEntity with Role = result.NewRole }
            let context = result.NewContext
            let context = { context with Game = context.Game.UpdateEntity entity }
            context, entity
        | None ->
            let revealNormal () =
                let h = entity |> getQueriedHandler context.Main.Rng
                let name = entity |> getDeadName h
                let reveal = entity |> request.GetReveal name
                sendMessage { Type = Public ; Content = reveal }
                let entity = { entity with State.Dead = { Dead = true ; Name = name } } |> updateOnDead request.DeadType
                let context = { context with Game = context.Game.UpdateEntity entity }
                context, entity
            match entity.Role with
            | :? IRoleLeaf as leaf when leaf.Fury |> not ->
                let context, entity = revealNormal ()
                sendMessage { Type = Public ; Content = $"{entity.Player.Name}是叶子" }
                let entity = { entity with
                                   State.Dead.Dead = false
                                   State.LeafProtected = Some true
                                   Role = leaf.SetFury () }
                let context = { context with Game = context.Game.UpdateEntity entity }
                context, entity
            | :? IRoleLeaf ->
                sendMessage { Type = Public ; Content = "叶子是叶子" }
                let entity = { entity with State.Dead = { Dead = true ; Name = "叶子" } } |> updateOnDead request.DeadType
                let context = { context with Game = context.Game.UpdateEntity entity }
                context, entity
            | _ ->
                revealNormal ()
        
    let updateOnNightStartRequestDead (context: RoleContext) (entity: Entity) =
        // 彩怪复活与暴毙
        let updateRebornState (reborn : RebornState option) =
            let updateReborn r =
                if r.ReadyRound > 0 then { r with ReadyRound = r.ReadyRound - 1 }
                elif r.RebornRound > 0 then { r with RebornRound = r.RebornRound - 1 }
                else r
            if reborn.IsNone then reborn
            else reborn |> Option.map updateReborn
        let state = { entity.State with Reborn = updateRebornState entity.State.Reborn }
        let state =
            if state |> EntityState.isDead && state.Reborn.IsSome && state.Reborn.Value.Reborn then
                sendMessage { Type = ToPlayer entity.Player ; Content = "你复活了" }
                { state with Dead.Dead = false }
            else
                state
        
        let entity = { entity with State = state }
        let context, entity =
            if state |> EntityState.isDead |> not
               && state.Reborn.IsSome && state.Reborn.Value.Reborn |> not then
                let request = DeadRequest.New Force
                requestDead request context entity
            else
                context, entity
        
        if entity.State |> EntityState.isDead then context, entity else
        
        // 爬行者炸弹
        let rec bombKill count (c: RoleContext) (e: Entity) =
            if count <= 0 then c, e else
            sendMessage { Type = Public ; Content = $"{entity.Player.Name}身上的炸药爆炸了！" }
            let c, e = requestDead (DeadRequest.New Kill) c e
            if e.State |> EntityState.isDead then c, e else
            bombKill (count - 1) c e
        let context, entity = bombKill entity.State.Bomb context entity
        if entity.State |> EntityState.isDead then context, entity else
        
        // myz 威胁
        let context, entity =
            if entity.State.Threaten.IsNone || entity.State |> EntityState.isThreatenDead |> not then
                context, entity
            else
            let myz = entity.State.Threaten.Value
            let source = myz.Source
            if context.Game.GetEntity source |> getState |> EntityState.isDead then
                context, entity
            else
                
            sendMessage { Type = Public ; Content = $"{entity.Player.Name}无视了威胁！" }
            requestDead (DeadRequest.New Kill) context entity
        if entity.State |> EntityState.isDead then context, entity else
         
         // 身份各自处理（粉侠，彩怪复活后会暴毙的角色等）
        let list = entity.Role |> Role.getNightStartDeadRequest
        let rec loop (list: DeadRequest list) (context: RoleContext) (entity: Entity) =
            let request = list.Head
            let context, entity = requestDead request context entity
            if entity.State |> EntityState.isDead || list.Length <= 1 then
                context, entity
            else
                loop list.Tail context entity
        if list.Length = 0 then context, entity
        else loop list context entity
       
    let updateOnDayStartRequestDead (context: RoleContext) (entity: Entity) =
        if entity.State |> EntityState.isDead then context, entity else
         
         // 身份各自处理（彩怪失去所有彩条等）
        let list = entity.Role |> Role.getDayStartDeadRequest
        let rec loop (list: DeadRequest list) (context: RoleContext) (entity: Entity) =
            let request = list.Head
            let context, entity = requestDead request context entity
            if entity.State |> EntityState.isDead || list.Length <= 1 then
                context, entity
            else
                loop list.Tail context entity
        if list.Length = 0 then context, entity
        else loop list context entity
    
    // in game update
    
    let private updatePaoXianParty (main : MainContext) entity =
        let roll = main.Roll
        if entity |> getCharaType <> PaoXian
            || entity.State.PaoXianParty.Length = roll.BoomCount - 1 then
            entity
        else

        let party = entity.State.PaoXianParty
        let members = roll.Rolls |> List.filter (fun r ->
            r.Type <> PaoXian && r.Type.GetCamp () = Boom
            && party |> List.contains r.Player.Id |> not
            )
        if roll.Rolls.Length = 7 then
            let m = members |> List.randomChoiceWith main.Rng
            sendMessage { Type = ToPlayer entity.Player ; Content = $"队友：{m.Player.ToInGameString ()}" }
            { entity with State.PaoXianParty = m.Player.Id :: party }
        else
            let msg = members
                      |> List.map (fun m -> m.Player.ToInGameString ())
                      |> String.concat "，"
            sendMessage { Type = ToPlayer entity.Player ; Content = $"队友：{msg}" }
            { entity with State.PaoXianParty = members |> List.map (fun m -> m.Player.Id) }
        
    let updateOnNightStart main entity =
        if entity.State |> EntityState.isDead then entity else
        {
              entity with
                  State = entity.State |> EntityState.updateOnNightStart
                  Role = entity.Role |> Role.updateOnNightStart
        } |> updatePaoXianParty main
    
    let updateOnDayStart entity =
        if entity.State |> EntityState.isDead then entity else
        {
            entity with
                State = entity.State |> EntityState.updateOnDayStart
                Role = entity.Role |> Role.updateOnDayStart
        }
        
    // utils

    let createPendingSkill (handler: RoleHandler) (entity: Entity) =
        let role = handler.GetFromEntity entity
        {
            Handler = handler
            Type = role |> Role.getCharaType
            Source = entity.Player.Id
            Priority = role |> Role.getPriority
            Threaten = None
            Kidnapped = entity.State.Kidnapped.Length > 0
        }
        
    let getHandlerCharaType (handler: RoleHandler) entity =
        handler.GetFromEntity entity |> Role.getCharaType
        
    let getHandlerName (handler: RoleHandler) entity =
        (getHandlerCharaType handler entity).ToString ()
    
    let getValidCharaTypes entity =
        let handlers = entity.Role |> getValidHandlers
        handlers |> List.map (fun h -> h.GetFromEntity entity |> Role.getCharaType)