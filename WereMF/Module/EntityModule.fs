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
    let hasBarVote state =
        state.BarLeader = Some true
    let clearBarVote state =
        if hasBarVote state then { state with BarLeader = Some false }
        else state
    
    let isDead state =
        state.Dead.Dead

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
        
    let isThreatenDead (state: EntityState) =
        state.Threaten.IsSome && state.Threaten.Value.Type = QueuedDeath
        
    // in game marks
        
    let clearMarks entity =
        let entity = { entity with Smog = [] }
        let entity = { entity with Bug = None }
        let entity = { entity with Capsule = [] }
        let entity = { entity with Potion = [] }
        let entity = { entity with XianSong = 0 }
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
        let xian = repeat entity.XianSong "\U0001F36A"
        let cap = repeat entity.CapsuleCount "\U0001F48A"
        let drop = repeat entity.PotionCount "\U0001F4A7"
        smog + bug + xian + cap + drop
    
    // in game update
        
    let updateOnNightStart entity =
        {
             entity with
                LeafProtected = updateNightOptionBool entity.LeafProtected
                Kidnapped = []
                Threaten = None
                XianSong = 0
                Bomb = 0
                QueuedBomb = 0
                JiaoHuaVoteBlocked = false
        }
        
    let updateOnDayStart entity =
        let removeRes l =
            l |> List.map (fun i -> i - 1) |> List.filter (fun i -> i > 0)
        {
             entity with
                Dead.Name = ""
                LeafProtected = updateNightOptionBool entity.LeafProtected
                Smog = entity.Smog |> removeRes
                Capsule = entity.Capsule |> removeRes
                Potion = entity.Potion |> removeRes
                Milk = entity.Milk.UpdateOnDayStart ()
                JiaoHuaBlocked = 0
                JiaoHuaProtected = false
        }
        
    let updateOnDead state=
        {
            EntityState.New() with
                Dead = state.Dead
                BarLeader = state.BarLeader
                PaoXianParty = state.PaoXianParty
                Reversed = state.Reversed
        }
        
    let updateOnDeadButReborn state=
        { state with Bomb = 0 } |> clearMarks
    
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
        
    let getHandlerCharaType (handler: RoleHandler) (entity: Entity) =
        entity |> handler.GetFromEntity |> Role.getCharaType
        
    let getHandlerName handler (entity: Entity) =
        let chara = entity |> getHandlerCharaType handler
        match handler with
        | KirbyHandler _ -> $"{chara.ToString()}{Kirby.ToString()}"
        | _ -> chara.ToString()
        
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
        | Some v -> entity |> getHandlerName v
        | None -> "???"
        
    let getSummaryName entity =
        let reversed = if entity.State.Reversed then "反·" else ""
        let barLeader = if entity.State |> EntityState.isBarLeader then "（吧主）" else ""
        reversed + (entity.Role |> Role.getSummaryName) + barLeader
        
    let getNightSummary entity =
        if entity.State.Dead.Name <> "" then
            $"{entity.Player.Id.ToCircleString()}【{entity.State.Dead.Name}】"
        else
            $"{entity.Player.Id.ToCircleString()}{getInGameName entity}" +
            $"{entity.State |> EntityState.getTopMark} {entity.State |> EntityState.getBuffMark}"

    let getDaySummary entity =
        if entity.State.Dead.Name <> "" then
            $"{entity.Player.Id.ToCircleString()}【{entity.State.Dead.Name}】"
        else
            $"{entity.Player.Id.ToCircleString()}{getInGameName entity}" +
            $"{entity.State |> EntityState.getTopMark}"
        
    let getSummary (entity: Entity) =
        entity.Player.ToInGameString () + ": " + getSummaryName entity
        
    let printSummaryWith printer entities=
        entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

    let printNightSummary entities =
        entities |> printSummaryWith getNightSummary
        
    let printDaySummary entities =
        entities |> printSummaryWith getDaySummary

    let printSummary entities =
        entities |> printSummaryWith getSummary
    
    // in game judge
    
    let isLeafProtectedFresh entity =
        entity.State.LeafProtected = Some true
    let isLeafProtected source (entity: Entity) =
        entity.Player.Id <> source && entity.State.LeafProtected.IsSome
    let isJiaoHuaProtected source (entity: Entity) =
        entity.Player.Id <> source && entity.State.JiaoHuaProtected
    let canBeSelected source entity =
        not (entity |> isJiaoHuaProtected source || entity |> isLeafProtected source
             || entity.State.Smog |> List.exists (fun i -> i > 1))
    let canBeSelectedWithSmog source entity =
        not (entity |> isJiaoHuaProtected source || entity |> isLeafProtected source)
    let canBeVoted source entity =
        entity |> isLeafProtected source |> not
    let canVote entity =
        not entity.State.JiaoHuaVoteBlocked
    
    // dead check
    
    let updateOnDead dead entity =
        {
            entity with
                State = entity.State |> EntityState.updateOnDead
                Role = entity.Role |> Role.updateOnDead dead
        }
    
    let requestDead (request: DeadRequest) (context: DeadContext) =
        let entity, bind = context
        let header = request.GetName entity
        let reason = match request.DeadType with
                     | Kill -> "死了"
                     | Sudden -> "暴毙了"
                     | Force -> "暴毙了"
                     | Vote -> "出局"
        sendMessage { Type = Public ; Content = $"{header}{reason}" }
        let context, result = entity.Role |> tryPreventDead request.DeadType context
        if result.IsSome then
            let value = result.Value
            let entity, (main, game) = context
            let entity = { entity with Role = value.NewRole
                                       State =
                                           entity.State
                                           |> EntityState.updateOnDeadButReborn
                                           |> value.StateSetter }
            let game = game.UpdateEntity entity
            sendMessage { Type = Public ; Content = $"但是{header}复活了" }
            entity, (main, game)
        else
            let revealNormal () =
                let entity, (main, game) = context
                let h = entity |> getQueriedHandler main.Rng
                let name = entity |> getDeadName h
                let reveal = entity |> request.GetReveal name
                sendMessage { Type = Public ; Content = reveal }
                let entity = { entity with State.Dead = { Dead = true ; Name = name } } |> updateOnDead request.DeadType
                let game = game.UpdateEntity entity
                entity, (main, game)
            match entity.Role with
            | :? IRoleLeaf as leaf when leaf.Fury |> not ->
                let context = revealNormal ()
                let entity, (main, game) = context
                sendMessage { Type = Public ; Content = $"{entity.Player.Name}是叶子" }
                let entity = { entity with
                                   State.Dead.Dead = false
                                   State.Dead.Name = ""
                                   State.LeafProtected = Some true
                                   Role = leaf.SetFury () }
                let game = game.UpdateEntity entity
                entity, (main, game)
            | :? IRoleLeaf ->
                sendMessage { Type = Public ; Content = "叶子是叶子" }
                let entity, (main, game) = context
                let entity = { entity with State.Dead = { Dead = true ; Name = "叶子" } } |> updateOnDead request.DeadType
                let game = game.UpdateEntity entity
                entity, (main, game)
            | _ ->
                revealNormal ()
    
    let private isDead (context: DeadContext) =
        match context with
        | entity, _ when entity.State |> EntityState.isDead -> true
        | _ -> false
    
    let rec requestDeadList (list: DeadRequest list) (c: DeadContext) =
        if list.IsEmpty then c else
        let request = list.Head
        let c = requestDead request c
        if c |> isDead then c
        else requestDeadList list.Tail c
    
    let private updateOnNightStartReborn (context: DeadContext) =
        let updateRebornState (reborn : RebornState option) =
            let updateReborn r =
                if r.ReadyRound > 0 then { r with ReadyRound = r.ReadyRound - 1 }
                elif r.RebornRound > 0 then { r with RebornRound = r.RebornRound - 1 }
                else r
            if reborn.IsNone then reborn
            else reborn |> Option.map updateReborn
        
        let entity, (main, game) = context
        let state = { entity.State with Reborn = updateRebornState entity.State.Reborn }
        let state =
            if state |> EntityState.isDead && state.Reborn.IsSome && state.Reborn.Value.Reborn then
                sendMessage { Type = ToPlayer entity.Player ; Content = "你复活了" }
                { state with Dead.Dead = false }
            else
                state
        
        let entity = { entity with State = state }
        let game = game.UpdateEntity entity
        let entity, (main, game) =
            if state |> EntityState.isDead |> not
               && state.Reborn.IsSome && state.Reborn.Value.Reborn |> not then
                let request = DeadRequest.New Force
                let context = requestDead request (entity, (main, game))
                context
            else
                entity, (main, game)
        
        entity, (main, game)
    
    let private updateOnNightStartCreeper (context : DeadContext) =
        let e, _ = context
        if e.State.Bomb <= 0 then context else
            
        sendMessage { Type = Public ; Content = $"{e.Player.Name}身上的炸药爆炸了！" }
        requestDead (DeadRequest.New Kill) context
    
    let private updateOnNightStartXianSong (context : DeadContext) =
        let e, _ = context
        if e.State.XianSong <= 0 then context else
            
        sendMessage { Type = Public ; Content = $"{e.Player.Name}身上的咸松球爆炸了！" }
        requestDead (DeadRequest.New Sudden) context
    
    let private updateOnNightStartMyz (context : DeadContext) =
        let entity, (main, game) = context
        if entity.State |> EntityState.isThreatenDead |> not then
            context
        else
        
        let threaten = entity.State.Threaten.Value
        let src = threaten.Source
        let sEntity = game.GetEntity src
        if sEntity.State |> EntityState.isDead then context else
        
        sendMessage { Type = Public ; Content = $"{entity.Player.Name}无视了威胁！" }
        requestDead (DeadRequest.New Kill) context
    
    let updateOnNightStartRequestDead (context: DeadContext) =
        // 彩怪复活计时器
        let context = updateOnNightStartReborn context
        if context |> isDead then context else
            
        // 身份各自处理（粉侠，彩怪复活后会暴毙的角色等）
        let entity, _ = context
        let list = entity.Role |> Role.getNightStartDeadRequest
        let context = requestDeadList list context
        if context |> isDead then context else
        
        // 炸药与咸松球
        let context = updateOnNightStartCreeper context
        if context |> isDead then context else
            
        let context = updateOnNightStartXianSong context
        if context |> isDead then context else
        
        // myz威胁
        updateOnNightStartMyz context
       
    let updateOnDayStartRequestDead (context: DeadContext) =
        if context |> isDead then context else
         
         // 身份各自处理（彩怪失去所有彩条等）
        let entity, _ = context
        let list = entity.Role |> Role.getDayStartDeadRequest
        requestDeadList list context
    
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
        
    let updateOnNightInit entity =
        if entity.State |> EntityState.isDead then entity else
        { entity with Role = entity.Role |> Role.updateOnNightInit }
    
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
    
    // vote update
    
    let voteSourceFilter (game: GameContext) = function
        | Ok id when game.HasEntity id |> not ->
            Error "该玩家不存在"
        | Ok id ->
            let e = game.GetEntity id
            if e.State |> EntityState.isDead then Error "该玩家已死亡"
            elif e |> canVote |> not then Error "该玩家不能投票"
            else Ok id
        | value -> value

    let voteTargetFilter source (game: GameContext) = function
        | Ok id when id <= PlayerId 0 -> Ok (PlayerId 0)
        | Ok id when game.HasEntity id |> not ->
            Error "目标不存在"
        | Ok id ->
            let e = game.GetEntity id
            if e.State |> EntityState.isDead then Error "目标已死亡"
            elif e |> canBeVoted source |> not then Error "目标不可选中"
            else Ok id
        | value -> value
        
    let private updateThreatenOnVoteStart (game: GameContext) (day: DayContext) (entity: Entity) =
        if entity.State.Threaten.IsNone then day, entity else
            
        let threaten = entity.State.Threaten.Value
        let src = threaten.Source
        let sEntity = game.GetEntity src
        if sEntity.State |> EntityState.isDead then day, { entity with State.Threaten = None } else
        
        match threaten.Type with
        | QueuedDeath -> day, entity
        | DayVote (target, force) ->
            if Ok entity.Player.Id |> voteSourceFilter game |> Result.isError then
                sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
                day, { entity with State.Threaten = None }
            else

            if Ok target |> voteTargetFilter entity.Player.Id game |> Result.isOk then
                if force |> not then day, entity else
                let msg = if target <= PlayerId 0 then $"{entity.Player.Name}被强制弃票"
                          else $"{entity.Player.Name}被强制把票投给{target}"
                sendMessage { Type = Public ; Content = msg }
                let vote = day.GetPlayerVote entity.Player.Id
                let vote = { vote with Target = Some target ; Confirmed = true }
                let day = day.SetPlayerVote vote
                day, { entity with State.Threaten = None }
            else
                sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
                day, { entity with State.Threaten = None }
    
    let private updateThreatenOnVoteEnd (day: DayContext) (entity : Entity) =
        let t = entity.State.Threaten
        if t.IsNone then entity else
        let t = t.Value
        
        match t.Type with
        | QueuedDeath -> entity
        | DayVote (target, _) ->
            let real = (day.GetPlayerVote entity.Player.Id).GetTarget()
            if real = target then { entity with State.Threaten = None }
            else { entity with State.Threaten = Some { t with Type = QueuedDeath } }
    
    let private updateBombOnVoteEnd (entity : Entity) (day: DayContext) (game : GameContext)  =
        let t = (day.GetPlayerVote entity.Player.Id).GetTarget()
        let b = entity.State.QueuedBomb
        if t <= PlayerId 0 then
            let entity = { entity with State.QueuedBomb = 0 ; State.Bomb = entity.State.Bomb + b }
            game.UpdateEntity entity
        else

        let entity = { entity with State.QueuedBomb = 0 }
        let game = game.UpdateEntity entity
        let te = game.GetEntity t
        let te = { te with State.Bomb = te.State.Bomb + b }
        game.UpdateEntity te
    
    let updateOnVoteStart (entity : Entity) (game : GameContext) (day: DayContext) =
        // 脚滑人禁票等
        let game, role = entity.Role |> updateOnVoteStart entity.Player game
        let entity = { entity with Role = role }
        let game = game.UpdateEntity entity
        
        if entity.State |> EntityState.isDead then game, day else
        let day, entity = updateThreatenOnVoteStart game day entity
        let game = game.UpdateEntity entity
        game, day
    
    let updateOnVoteEnd (entity : Entity) (game : GameContext) (day: DayContext)=
        let entity = updateThreatenOnVoteEnd day entity
        
        // 江仙投票等
        let day, role = entity.Role |> updateOnVoteEnd entity game day
        let entity = { entity with Role = role }
        let game = game.UpdateEntity entity
        
        let game = updateBombOnVoteEnd entity day game
        game, day
    
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
    
    let getValidCharaTypes entity =
        let handlers = entity.Role |> getValidHandlers
        handlers |> List.map (fun h -> entity |> getHandlerCharaType h)