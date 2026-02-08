module WereMF.Module.Game

open WereMF.State

let private printSummaryWith printer (game: GameContext) =
    game.Entities |> List.map (fun e -> e |> printer) |> String.concat "\n"

let printNightSummary (game: GameContext) =
    game |> printSummaryWith Entity.getNightSummary
    
let printDaySummary (game: GameContext) =
    game |> printSummaryWith Entity.getDaySummary

let printSummary (game: GameContext) =
    game |> printSummaryWith Entity.getSummary