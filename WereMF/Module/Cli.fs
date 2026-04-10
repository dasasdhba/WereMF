module WereMF.Module.Cli

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Data
open FSharpPlus
open WereMF.Common

type private CliOptions = {
    Help: bool
    Api: bool
    Config: string option
    Seed: int option
}

let private args = Environment.GetCommandLineArgs() |> Array.skip 1 |> Array.toList

let rec private parseArgs args acc =
    match args with
    | [] -> acc
    | "--help" :: rest -> parseArgs rest { acc with Help = true }
    | "--api" :: rest -> parseArgs rest { acc with Api = true }
    | "--config" :: path :: rest -> parseArgs rest { acc with Config = Some path }
    | "--seed" :: seed :: rest -> parseArgs rest { acc with Seed = Int32.Parse seed |> Some }
    | [ "--config" ] -> failwith "--config requires a path"
    | [ "--seed" ] -> failwith "--seed requires a int number"
    | unknown :: _ -> failwith $"Unknown argument: {unknown}"

let private options = parseArgs args { Help = false; Api = false; Config = None ; Seed = None }

if options.Help then
    printfn "Usage: WereMF [--help] [--api] [--config <path>] [--seed <int>]"
    exit 0

let cliConfig = defaultArg options.Config "config.json"
let cliApi = options.Api
let cliSeed = options.Seed

let mutable cliUndo : string list = []
let mutable cliRedo : string list = []
let mutable cliReplay : string list = []
let mutable cliSilent : bool = false
let mutable cliLogName : string = ""
let mutable cliLog : bool = false

type MessageType =
    | Internal
    | Public
    | ToPlayer of Player

type Message =
    {
        Type : MessageType
        Content : string
        Api : string
        Data : JsonValue
    }
    override this.ToString() =
        match this.Type with
        | Internal -> $"[Internal] {this.Content}"
        | Public -> $"[Public] {this.Content}"
        | ToPlayer p -> $"[ToPlayer {p.ToCliString()}] {this.Content}"
    member this.ToJsonValue () = JsonValue.Record [|
        "api", JsonValue.String this.Api
        "message_type", (match this.Type with
                         | Internal -> JsonValue.String "internal"
                         | Public -> JsonValue.String "public"
                         | ToPlayer p -> JsonValue.String $"player_{p.Id}"
                         )
        "message_content", JsonValue.String this.Content
        "data", this.Data
    |]

let sendApi (message: Message) =
    if cliApi then Console.WriteLine (message.ToJsonValue().ToString())

let sendMessage (message : Message) =
    if cliLog then
        File.AppendAllText(cliLogName, message.ToString() + "\n", System.Text.Encoding.UTF8)
    
    if cliSilent then
        ()
    else
        Console.WriteLine (if cliApi then message.ToJsonValue().ToString() else message.ToString())

type RawMessage =
    {
        Type : MessageType
        Content : string
    }
    member this.ToMessage api = {
        Type = this.Type
        Content = this.Content
        Api = api
        Data = JsonValue.Null
    }
        
let sendRawMessage (raw : RawMessage) api =
    let message = raw.ToMessage api
    sendMessage message

type CommandType =
    | Undo
    | Redo
    | Restart
    | Reboot
    | Exit
    | NightSummary
    | DaySummary
    | VoteSummary
    | Summary
    | Rename of int * string
    | Log

exception CommandEx of CommandType

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
    | true, value -> Ok value
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

/// 解析多个玩家ID（支持最多 n 个，用空格分隔）
/// 输入 "0" 表示放弃
/// 返回 (PlayerId list, 是否放弃)
type ParseMultiPlayerResult =
    | GiveUp
    | Targets of PlayerId list

type ParseMultiPlayerConfig = {
    MaxCount : int
    MaxCountError : string option
    DuplicateError : string option
}

let parseMultiPlayerId (config: ParseMultiPlayerConfig) (input: string) : Result<ParseMultiPlayerResult, string> = monad {
    let! ids = parsePlayerIdList input
    if ids.Length > config.MaxCount then
        return! match config.MaxCountError with
                | Some msg -> Error msg
                | None -> Error $"超过最大数量限制（最多 {config.MaxCount} 个），输入了 {ids.Length} 个"
    elif ids.Length = 1 && ids.Head <= PlayerId 0 then GiveUp else
    // 检查重复
    let distinctIds = ids |> List.distinct
    if distinctIds.Length <> ids.Length then
        return! match config.DuplicateError with
                | Some msg -> Error msg
                | None -> Error "不能重复选择同一个玩家"
    else
        Targets ids
}

/// 通用多目标技能发送 parser
/// config: 多玩家解析配置
/// filter: 玩家过滤函数
/// createSkill: 创建技能的函数
/// 返回 ISkill option list
let parseMultiSkill 
    (config: ParseMultiPlayerConfig) 
    (filter: Result<PlayerId, string> -> Result<PlayerId, string>)
    (createSkill: PlayerId -> Skill)
    (input: string) : Result<Skill option list, string> = monad {
    let! targets = parseMultiPlayerId config input
    match targets with
    | GiveUp -> [None]
    | Targets ids ->
        let results = ids |> List.map (fun id ->
            match Ok id |> filter with
            | Error e -> Error e
            | Ok i when i <= PlayerId 0 -> Error "想放弃请仅输入 0"
            | _ -> Ok id
        )
        
        let errors = results |> List.choose (function Error e -> Some e | _ -> None)
        if errors.Length > 0 then
            return! Error (errors |> List.head)
        else
            let validIds = results |> List.choose (function Ok id -> Some id | _ -> None)
            let skills = validIds |> List.map (fun id ->
                createSkill id |> Some
            )
            skills
}
    
let parseCommand (input : string) =
    match input with
    | "\\undo" ->
        if cliUndo |> List.isEmpty then
            sendRawMessage { Type = Internal ; Content = "撤销列表为空" } "command_error"
            Error true
        else
            Ok Undo
    | "\\redo" ->
        if cliRedo |> List.isEmpty then
            sendRawMessage { Type = Internal ; Content = "重做列表为空" } "command_error"
            Error true
        else
            Ok Redo
    | "\\reboot" ->
        if cliUndo |> List.isEmpty then
            sendRawMessage { Type = Internal ; Content = "游戏还没开始" } "command_error"
            Error true
        else
            Ok Reboot
    | "\\restart" ->
        if cliUndo |> List.isEmpty  then
            sendRawMessage { Type = Internal ; Content = "请先输入玩家" } "command_error"
            Error true
        else
            Ok Restart
    | s when s.StartsWith "\\rename" ->
        if cliUndo |> List.isEmpty  then
            sendRawMessage { Type = Internal ; Content = "请先输入玩家" } "command_error"
            Error true
        else
        
        let args = splitInputList s
        match args.Tail with
        | [ id ; name ] ->
            let id = parseInt id
            match id with
            | Error e ->
                sendRawMessage { Type = Internal ; Content = e } "command_error"
                Error true
            | Ok id ->
                Ok (Rename (id, name))
        | _ ->
            sendRawMessage { Type = Internal ; Content = "请输入要重命名的玩家编号和新的玩家名" } "command_error"
            Error true
    | "\\log" -> Ok Log
    | "\\night" -> Ok NightSummary
    | "\\day" -> Ok DaySummary
    | "\\summary" -> Ok Summary
    | "\\vote" -> Ok VoteSummary
    | "\\exit" -> Ok Exit
    | _ -> Error false
    
let requestInputWithMessage (message : Message) (parser : string -> Result<'a, string>) =
    let rec loop () =
        let input = if cliReplay |> List.isEmpty then
                        cliLog <- false
                        cliSilent <- false
                        sendMessage message
                        Console.ReadLine()
                    else cliReplay.Head
        let command = parseCommand input
        match command with
        | Ok c -> raise (c |> CommandEx)
        | Error true -> loop ()
        | Error false ->
            match parser input with
            | Ok result ->
                if cliReplay |> List.isEmpty then
                    cliUndo <- cliUndo @ [input]
                    cliRedo <- []
                else
                    if cliLog then
                        File.AppendAllText(cliLogName, message.ToString() + "\n" + cliReplay.Head + "\n", System.Text.Encoding.UTF8)
                    cliReplay <- cliReplay.Tail
                result
            | Error msg ->
                sendRawMessage { Type = message.Type ; Content = msg } $"{message.Api}_parse_error"
                loop ()
    loop ()
    
let requestInputWithRawMessage (raw : RawMessage) (api : string) (parser : string -> Result<'a, string>) =
    let message = raw.ToMessage api
    requestInputWithMessage message parser