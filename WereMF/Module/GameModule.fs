module WereMF.Module.Game

open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.State

let involveIfDoge (target: Entity) (night: NightContext) (role: RoleContext)=
    if target.State |> EntityState.isDead |> not then role else
    let prots = night.PlayerStates |> List.filter (fun ps ->
        ps.Id |> role.Game.GetEntity |> getState |> EntityState.isDead |> not
        && ps.Doge.IsSome && ps.Doge.Value = target.Player.Id)
    let mutable r = role
    for ps in prots do
        let entity = role.Game.GetEntity ps.Id
        let name = entity.Player.Name
        let msg = $"{target.Player.Name}保护了{name}"
        sendMessage { Type = Public ; Content = msg }
        let request = DeadRequest.New Kill
        let c, _ = entity |> requestDead request r
        r <- c
    r

let private printSummaryWith printer entities=
    entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

let printNightSummary entities =
    entities |> printSummaryWith Entity.getNightSummary
    
let printDaySummary entities =
    entities |> printSummaryWith Entity.getDaySummary

let printSummary entities =
    entities |> printSummaryWith Entity.getSummary

