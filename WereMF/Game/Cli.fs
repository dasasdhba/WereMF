module WereMF.Game.Cli

open System
open System.Text.RegularExpressions
open FSharpPlus
open WereMF.Type.Chara
open WereMF.Type.Player

let mutable cliUndo : string list = []
let mutable cliRedo : string list = []
let mutable cliReplay : string list = []
let mutable cliSilent : bool = false

type MessageType =
    | Internal
    | Public
    | ToPlayer of Player

type Message =
    {
        Type : MessageType
        Content : string
    }
    override this.ToString() =
        match this.Type with
        | Internal -> $"[Internal] {this.Content}"
        | Public -> $"[Public] {this.Content}"
        | ToPlayer p -> $"[ToPlayer {p.ToCliString()}] {this.Content}"

let sendMessage (message : Message) =
    if cliSilent then
        ()
    else
        Console.WriteLine message

type CommandType =
    | Undo
    | Redo
    | Restart
    | Reboot

let parseCommand (input : string) =
    match input with
    | "\\undo" ->
        if cliUndo |> List.isEmpty then
            sendMessage { Type = Internal ; Content = "撤销列表为空" }
            Error true
        else
            Ok Undo
    | "\\redo" ->
        if cliRedo |> List.isEmpty then
            sendMessage { Type = Internal ; Content = "重做列表为空" }
            Error true
        else
            Ok Redo
    | "\\reboot" ->
        if cliUndo |> List.isEmpty then
            sendMessage { Type = Internal ; Content = "游戏还没开始" }
            Error true
        else
            Ok Reboot
    | "\\restart" ->
        if cliUndo.Length <= 1 then
            sendMessage { Type = Internal ; Content = "请先输入玩家" }
            Error true
        else
            Ok Restart
    | _ -> Error false

let requestInputWith (msg : string) (parser : string -> Result<'a, string>) =
    let rec loop () =
        let input = if cliReplay |> List.isEmpty then
                        cliSilent <- false
                        Console.WriteLine msg
                        Console.ReadLine()
                    else cliReplay.Head
        let command = parseCommand input
        match command with
        | Ok c -> Error c
        | Error true -> loop ()
        | Error false ->
            match parser input with
            | Ok result ->
                if cliReplay |> List.isEmpty then
                    cliUndo <- cliUndo @ [input]
                    cliRedo <- []
                else
                    cliReplay <- cliReplay.Tail
                Ok result
            | Error msg -> 
                Console.WriteLine msg
                loop ()
    loop ()
    
let requestInputWithMessage (message : Message) (parser : string -> Result<'a, string>) =
    let mParser = fun s ->
        match parser s with
        | Ok result -> Ok result
        | Error msg ->
            let errMsg = { Type = message.Type ; Content = msg }
            Error (errMsg.ToString())
    requestInputWith (message.ToString()) mParser
    
let splitInputList (input: string) : string list =
    let pattern = "\"([^\"]*)\"|(\\S+)"
    let matches = Regex.Matches(input, pattern)
    [ for m in matches ->
        if m.Groups[1].Success then m.Groups[1].Value
        else m.Groups[2].Value ]
    
let parseBool (input: string) : Result<bool, string> =
    let low = input.ToLower()
    match low with
    | "1" | "true" | "y" | "yes" | "是" -> Ok true
    | "0" | "false"| "n" | "no" | "否" -> Ok false
    | _ -> Error "未知格式"
    
let parseInt (s: string) =
    match Int32.TryParse(s) with
    | (true, value) -> Ok value
    | _ -> Error "未知格式"
    
let parseIntList (input :string) =
    let strList = splitInputList input
    let someInt = strList |> List.map parseInt
    if someInt |> List.exists (function Error _ -> true | _ -> false) then
        Error "未知格式"
    else
        Ok (someInt |> List.choose (function Ok v -> Some v | _ -> None))
        
let parseChara input = CharaType.Create input

/// Parse string as Chara list,
/// duplicates will be cut
let parseCharaList (input: string) =
    let strList = splitInputList input
    let someChara = strList |> List.map parseChara
    
    let errors = someChara
                 |> List.choose (function Error msg -> Some msg | _ -> None)
                 |> List.distinct
    let successes = someChara
                    |> List.choose (function Ok v -> Some v | _ -> None)
                    |> List.distinct
    
    if List.isEmpty errors then Ok successes
    else Error (errors |> String.concat "; ")

let parsePlayerId (input : string)= monad {
    let! r = parseInt input
    PlayerId r
}

let parsePlayerIdList (input : string)= monad {
    let! r = parseIntList input
    r |> List.map PlayerId
}