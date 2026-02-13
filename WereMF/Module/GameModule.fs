module WereMF.Module.Game

open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.State

let blockIfLeaf (target: Entity) (night: NightContext) =
    if target.State.LeafProtected.IsNone then night else
    let state = night.GetPlayerState target.Player.Id
    let state = { state with Blocked = true }
    night.SetPlayerState state

let involveIfDoge (target: Entity) (context: SkillContext)=
    let (main, game), night = context
    if target.State |> EntityState.isDead |> not then context else
    let prots = night.PlayerStates |> List.filter (fun ps ->
        ps.Id |> game.GetEntity |> getState |> EntityState.isDead |> not
        && ps.Doge |> List.contains target.Player.Id)
    let mutable c = context
    for ps in prots do
        let (main, game), night = c
        let ps = { ps with Doge = ps.Doge |> List.filter (fun id -> id <> target.Player.Id) }
        let night = night.SetPlayerState ps
        let entity = game.GetEntity ps.Id
        let name = entity.Player.Name
        let msg = $"{target.Player.Name}保护了{name}"
        sendMessage { Type = Public ; Content = msg }
        let request = DeadRequest.New Kill
        let dead = entity, (main, game)
        let entity, (main, game) = requestDead request dead
        let night = blockIfLeaf entity night
        c <- ((main, game), night)
    c

let printSummaryWith printer entities=
    entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

let printNightSummary entities =
    entities |> printSummaryWith Entity.getNightSummary
    
let printDaySummary entities =
    entities |> printSummaryWith Entity.getDaySummary

let printSummary entities =
    entities |> printSummaryWith Entity.getSummary

