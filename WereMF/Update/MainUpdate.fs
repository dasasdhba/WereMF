module WereMF.Update.Main

open System
open System.IO
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
            Api = ApiType.CliNightSummary
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
                Api = ApiType.CliDaySummary
                Data = game.Context.ToJsonValue ()
            }
        | _ -> ()
    | _ -> ()

let rec private updateMain main : MainState =
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
            seed <- DateTime.UtcNow.Ticks.GetHashCode()
            sendRawMessage { Type = Internal ; Content = $"游戏种子：{seed}" } ApiType.CliGameSeed
            cliUndo <- []
            cliRedo <- []
            cliReplay <- []
            cliSilent <- false
            updateMain (MainState.New seed)
        | Restart ->
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
        | Log ->
            let text = $"游戏种子：{seed}\n玩家：\n"
            let summary = tryGetSummaryWith main Entity.getSummary
            let now = DateTime.Now.ToString("yyMMdd_HHmmss")
            let log = $"WereMF_{now}.log"
            File.AppendAllText(log, text + summary + "\n\n", System.Text.Encoding.UTF8)
            
            cliLogName <- log
            cliLog <- true
            cliReplay <- cliUndo
            cliSilent <- true
            updateMain (MainState.New seed)
        | Exit ->
            main
    | ex ->
        printfn "%s" ex.Message
        reraise()

let launchMain () =
    sendRawMessage { Type = Internal ; Content = $"游戏种子：{seed}" } ApiType.CliGameSeed
    updateMain (MainState.New seed)

