open System
open FSharpPlus.Data
open WereMF.State.MainState
open WereMF.Game.Cli
open WereMF.Update.Init
open WereMF.Update.Roll

let mutable seed = DateTime.UtcNow.Ticks.GetHashCode()

let rec updateMain main : MainState =
    let run runner =
        let (r, c) = State.run runner main.Context
        match r with
        | Ok s -> Ok (s, c)
        | Error e -> Error e
    
    let next =
       match main.Status with
       | WaitForPlayers -> run (initPlayers ())
       | Roll -> run (rollUpdate ())
       | _ -> Error Reboot
    
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
            updateMain (createMainState seed)
        | Redo ->
            cliUndo <- cliUndo @ [cliRedo.Head]
            cliRedo <- cliRedo.Tail
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (createMainState seed)
        | Reboot ->
            cliUndo <- []
            cliRedo <- []
            cliReplay <- []
            cliSilent <- false
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            updateMain (createMainState seed)
        | Restart ->
            cliUndo <- [cliUndo.Head]
            cliRedo <- []
            cliReplay <- cliUndo
            cliSilent <- true
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            updateMain (createMainState seed)

updateMain (createMainState seed) |> ignore