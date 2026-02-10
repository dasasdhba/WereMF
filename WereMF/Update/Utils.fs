module WereMF.Module.Utils

open WereMF.Common
open WereMF.Module.Role

let createPendingSkills (entities: Entity list) =
    let mutable result = []
    for e in entities do
        let h = getPendingHandlers e.Player e.Role
        let s = h |> List.map (fun u -> e |> Entity.createPendingSkill u)
        result <- result @ s
    result

let private printSummaryWith printer entities=
    entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

let printNightSummary entities =
    entities |> printSummaryWith Entity.getNightSummary
    
let printDaySummary entities =
    entities |> printSummaryWith Entity.getDaySummary

let printSummary entities =
    entities |> printSummaryWith Entity.getSummary