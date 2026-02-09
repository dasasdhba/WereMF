namespace WereMF.Module

open System
open WereMF.Common
open WereMF.Module.Cli
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
        
    // in game marks
        
    let clearMarks entity =
        let entity = { entity with Smog = [] }
        let entity = { entity with Bug = 0 }
        let entity = { entity with Capsule = [] }
        let entity = { entity with Potion = [] }
        let entity = { entity with XianSong = 0 }
        entity
        
    let getTopMark entity =
        let voteBlock = if entity.JiaoHuaVoteBlocked then "\u2716" else ""
        let protect = if entity.JiaoHuaProtected then "\U0001F6E1" else ""
        let roleBlock = if entity.JiaoHuaBlocked > 0 then "\u274c" else ""
        let leafBlock = if entity.LeafProtected then "\u274e" else ""
        voteBlock + protect + roleBlock + leafBlock
        
    let getBuffMark (entity : EntityState)=
        let repeat n s =
            if n <= 0 then ""
            elif n = 1 then s
            else [ for i in 1 .. n -> s ] |> List.map (fun i -> s) |> String.concat ""
        let smog = repeat entity.SmogCount "\u2601"
        let bug = repeat entity.Bug "\U0001F41E"
        let xian = repeat entity.XianSong "\U0001F36A"
        let cap = repeat entity.CapsuleCount "\U0001F48A"
        let drop = repeat entity.PotionCount "\U0001F4A7"
        smog + bug + xian + cap + drop
        
    // in game judge
    
    let canBeSelected state =
        not (state.JiaoHuaProtected || state.LeafProtected || state.SmogCount > 0)
    let canBeSelectedWithSmog state =
        not (state.JiaoHuaProtected || state.LeafProtected)
    let canBeVoted state =
        not state.LeafProtected
    let canVote state =
        not state.JiaoHuaVoteBlocked
    
    // in game update
        
    let updateOnNightStart entity =
        let updateRebornState (reborn : RebornState option) =
            let updateReborn r =
                if r.ReadyRound > 0 then { r with ReadyRound = r.ReadyRound - 1 }
                elif r.RebornRound > 0 then { r with RebornRound = r.RebornRound - 1 }
                else r
            if reborn.IsNone then reborn
            else reborn |> Option.map updateReborn
        {
             entity with
                LeafProtected = false
                Kidnapped = None
                Threaten = None
                XianSong = 0
                Bomb = 0
                JiaoHuaVoteBlocked = false
                Reborn = entity.Reborn |> updateRebornState
        }
        
    let updateOnDayStart entity =
        let removeRes l =
            l |> List.map (fun i -> i - 1) |> List.filter (fun i -> i > 0)
        {
             entity with
                LeafProtected = false
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
        let reborn = match entity.State.Reborn with
                     | Some _ -> "（复活）"
                     | None -> ""
        reversed + entity.Player.Name + reborn
        
    let getDeadName() handler (entity: Entity) =
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
        
    // in game update
    
    let private updatePaoXianParty (main : MainContext) entity =
        let roll = main.Roll
        if entity.Role |> Role.getCharaType <> PaoXian
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
        {
              entity with
                  State = entity.State |> EntityState.updateOnNightStart
                  Role = entity.Role |> Role.updateOnNightStart
        } |> updatePaoXianParty main
    
    let updateOnDayStart entity =
        {
            entity with
                State = entity.State |> EntityState.updateOnDayStart
                Role = entity.Role |> Role.updateOnDayStart
        }
        
    let updateOnDead entity =
        {
            entity with
                State = entity.State |> EntityState.updateOnDead
                Role = entity.Role |> Role.updateOnDead
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
            Kidnapped = entity.State.Kidnapped.IsSome
            Blocked = false
        }
        
    let getHandlerCharaType (handler: RoleHandler) entity =
        handler.GetFromEntity entity |> Role.getCharaType
        
    let getHandlerName (handler: RoleHandler) entity =
        (getHandlerCharaType handler entity).ToString ()