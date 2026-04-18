module WereMF.Update.Main

open System
open FSharpPlus.Data
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Update.Game
open WereMF.Update.Init
open WereMF.Update.Roll

let mutable private seed = defaultArg cliSeed (DateTime.UtcNow.Ticks.GetHashCode())

let private tryGetSummaryWith (main: MainState) printer =
    match main.Status with
    | Game game ->
        let context = game.Context
        Entity.printSummaryWith printer context.Entities
    | _ -> ""

let private tryPrintWith (main: MainState) printer =
    match main.Status with
    | Game game ->
        let context = game.Context
        sendMessage {
            Type = Public
            Content = $"\n{Entity.printSummaryWith printer context.Entities}"
            Api = ApiType.CliGameSummary
            Data = game.Context.ToJsonValue ()
        }
    | _ -> ()

let rec private tryPrintVote (main: MainState) =
    match main.Status with
    | Game game ->
        match game.Status with
        | Day day ->
            sendMessage {
                Type = Public
                Content = $"\n{Day.printVoteSummary game.Context day}"
                Api = ApiType.CliVoteSummary
                Data = game.Context.ToJsonValue ()
            }
        | _ -> ()
    | _ -> ()

let rec private updateMain main : MainState =
    let saveLogWith l command =
        let text = $"游戏种子：{seed}\n玩家：\n"
        let summary = tryGetSummaryWith main Entity.getSummary
        let log =
            if l = "" then
                let now = DateTime.Now.ToString("yyMMdd_HHmmss")
                $"WereMF_{now}.log"
            else
                l
        
        cliLogContent <- text + summary + "\n\n"
        cliLogName <- log
        cliLog <- true
        cliReplay <- if command <> "" then cliUndo @ [command] else cliUndo
        cliSilent <- true
        updateMain (MainState.New seed)
    
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
            saveLogWith "" "\exit" |> ignore
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            sendRawMessage { Type = Internal ; Content = $"游戏种子：{seed}" } ApiType.CliGameSeed
            cliUndo <- []
            cliRedo <- []
            cliReplay <- []
            cliSilent <- false
            updateMain (MainState.New seed)
        | Restart ->
            saveLogWith "" "\exit" |> ignore
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            sendRawMessage { Type = Internal ; Content = $"游戏种子：{seed}" } ApiType.CliGameSeed
            cliUndo <- [cliUndo.Head]
            cliRedo <- []
            cliReplay <- cliUndo
            cliSilent <- false
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
        | Rename (id, name) ->
            let newHead =
                let players = cliUndo.Head |> splitInputList
                if id > 0 && id <= players.Length then
                    players |> List.updateAt (id - 1) name |> String.concat " "
                else
                    cliUndo.Head
            cliUndo <- newHead :: cliUndo.Tail
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Log l ->
            saveLogWith l ""
        | Exit ->
            main
    | ex ->
        printfn "%s" ex.Message
        reraise()

let launchMain () =
    sendRawMessage { Type = Internal ; Content = $"游戏种子：{seed}" } ApiType.CliGameSeed
    updateMain (MainState.New seed)

