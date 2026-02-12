module WereMF.Module.Utils

open System
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Role.Bind
open WereMF.Role.Kirby

// ----------------------------------------------------------------------------------
// night skill

let executeSkill (context: SkillContext) (skill : Skill) =
    let source = skill.Sending.Pending.Source
    let sEntity = context.Game.GetEntity source
    let blocked = (context.Night.GetPlayerState source).Blocked
    if blocked then
        sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
        context, skill, false
    else
    
    // spring
    let skill = { skill with Sending = skill.Sending |> setSpring context }
    
    // cost
    let context, skill =
        match skill.Actor with
        | :? ISkillCost as cost ->
            let actor, context = (State.run (cost.Cost skill.Sending) context)
            let skill = { skill with Actor = actor }
            context, skill
        | _ -> context, skill
    
    // kirby
    let target = skill.Sending |> getRealTarget
    let context, success =
        if context.Game.HasEntity target |> not then context, false else
        
        let state = context.Night.GetPlayerState target
        if state.Kirby.IsNone then context, false else
        
        let kirby, handler = state.Kirby.Value
        let state = { state with Kirby = None }
        let context = { context with Night = context.Night.SetPlayerState state }
        
        let kEntity = context.Game.GetEntity kirby
        let chara = skill.Sending.Pending.Type
        if chara = Kirby then
            sendMessage { Type = ToPlayer kEntity.Player ; Content = "失败" }
            sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
            context, true
        else
            sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
            sendMessage { Type = ToPlayer kEntity.Player ; Content = chara.ToString () }
            let role = handler.GetFromEntity kEntity
            match role with
            | :? KirbyRole as k ->
                let k = { k with CopiedRole = Some (createRole context.Main.Roll chara) }
                let kEntity = kEntity |> handler.SetToEntity k
                let context = { context with Game = context.Game.UpdateEntity kEntity }
                context, true
            | _ ->
                context, true
    
    // general
    let skill, context, success =
        if success then skill, context, false else
        match skill.Actor with
        | :? ISkillExecute as exe ->
            let (actor, context), success =
                if skill |> canExecute context then
                    (State.run (exe.Execute skill.Sending) context), true
                else
                    sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
                    (skill.Actor, context), false
            let skill = { skill with Actor = actor }
            skill, context, success
        | _ -> skill, context, false
        
    let context = skill.Sending |> updateBugWith context
    context, skill, success

let createPendingSkills (entities: Entity list) (rng : Random) =
    let rec jiaoHuaBlock remaining (list: PendingSkill list) (player: Player) =
        if remaining = 0 || list.Length = 0 then list else
        let list = list |> List.randomShuffleWith rng
        let blocked = list.Head
        sendMessage { Type = ToPlayer player; Content = $"你的{blocked.Type.ToString()}被禁用" }
        jiaoHuaBlock (remaining - 1) list.Tail player
    let mutable result = []
    for e in entities do
        let h = getPendingHandlers e.Player e.Role
        let s = h |> List.map (fun u -> e |> Entity.createPendingSkill u)
        let s = jiaoHuaBlock e.State.JiaoHuaBlocked s e.Player
        result <- result @ s
    result

// ----------------------------------------------------------------------------------
// summary

let private printSummaryWith printer entities=
    entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

let printNightSummary entities =
    entities |> printSummaryWith Entity.getNightSummary
    
let printDaySummary entities =
    entities |> printSummaryWith Entity.getDaySummary

let printSummary entities =
    entities |> printSummaryWith Entity.getSummary