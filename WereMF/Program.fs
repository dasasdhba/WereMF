open System
open System.Text
open FSharpPlus.Data
open WereMF.State
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Update
open WereMF.Update.Game
open WereMF.Update.Init
open WereMF.Update.Roll

let mutable seed = DateTime.UtcNow.Ticks.GetHashCode()

let private tryPrintWith (main: MainState) printer =
    match main.Status with
    | Game game ->
        let context = game.Context
        sendMessage { Type = Public ; Content = $"\n{Entity.printSummaryWith printer context.Entities}" }
    | _ -> ()

let rec private tryPrintVote (main: MainState) =
    match main.Status with
    | Game game ->
        match game.Status with
        | Day day ->
            sendMessage { Type = Public ; Content = $"\n{Day.printVoteSummary game.Context day}" }
        | _ -> ()
    | _ -> ()

let rec updateMain main : MainState =
    try 
        let s, c =
           match main.Status with
           | InputPlayers -> State.run (initPlayers ()) main.Context
           | Roll -> State.run (rollUpdate ()) main.Context
           | Game game -> State.run (gameUpdate game) main.Context
        updateMain { main with Status = s; Context = c }
    with
    | CommandEx c ->
        match c with
        | Undo ->
            cliRedo <- cliRedo @ [cliUndo |> List.last]
            cliUndo <- if cliUndo.Length > 1 then
                           cliUndo[..(cliUndo.Length - 2)]
                       else
                           []
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Redo ->
            cliUndo <- cliUndo @ [cliRedo.Head]
            cliRedo <- cliRedo.Tail
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Reboot ->
            cliUndo <- []
            cliRedo <- []
            cliReplay <- []
            cliSilent <- false
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            updateMain (MainState.New seed)
        | Restart ->
            cliUndo <- [cliUndo.Head]
            cliRedo <- []
            cliReplay <- cliUndo
            cliSilent <- false
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            updateMain (MainState.New seed)
        | NightSummary ->
            tryPrintWith main Entity.getNightSummary
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | DaySummary ->
            tryPrintWith main Entity.getDaySummary
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | VoteSummary ->
            tryPrintVote main
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Summary ->
            tryPrintWith main Entity.getSummary
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Exit ->
            main
    | ex ->
        printfn "%s" ex.Message
        reraise()

Console.OutputEncoding <- Encoding.UTF8
Console.InputEncoding <- Encoding.UTF8

updateMain (MainState.New seed) |> ignore