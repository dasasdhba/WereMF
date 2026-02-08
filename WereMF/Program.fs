open System
open FSharpPlus.Data
open WereMF.State
open WereMF.Module.Cli
open WereMF.Update.Game
open WereMF.Update.Init
open WereMF.Update.Roll

let mutable seed = DateTime.UtcNow.Ticks.GetHashCode()

let rec updateMain main : MainState =
    let runState runner =
        let r, c = State.run runner main.Context
        r |> Result.map (fun s -> s, c)
    
    let next =
       match main.Status with
       | InputPlayers -> runState (initPlayers ())
       | Roll -> runState (rollUpdate ())
       | Game game -> runState (gameUpdate game)
    
    match next with
    | Ok (s, c) -> updateMain { main with Status = s; Context = c }
    | Error c ->
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
            cliSilent <- true
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            updateMain (MainState.New seed)

updateMain (MainState.New seed) |> ignore