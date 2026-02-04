module WereMF.Cli

open System
open System.Text.RegularExpressions
open FSharpPlus
open FSharpPlus.Data
open WereMF.Chara
open WereMF.GameState
open WereMF.Player

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
    Console.WriteLine message
    
let parseCommand (input : string) : State<GameStack, GameStack * bool option> =
    monad {
        let! stack = State.get
        match input with
        | "\\undo" ->
            match stack.Undo() with
            | Ok result ->
                do! State.put result
                return result, Some true
            | Error msg ->
                sendMessage { Type = Internal ; Content = msg }
                return stack, Some false
        | "\\redo" ->
            let! stack = State.get
            match stack.Redo() with
            | Ok result ->
                do! State.put result
                return stack, Some true
            | Error msg ->
                sendMessage { Type = Internal ; Content = msg }
                return stack, Some false
        | _ -> return stack, None
    }
    
let requestInput (parser : string -> Result<'a, string>) =
    let rec loop () =
        let input = Console.ReadLine()
        monad {
            let! stack, command = parseCommand input
            do! State.put stack
            if command = Some true then
                return stack, None
            elif command = Some false then
                return! loop ()
            else
                match parser input with
                | Ok result ->
                    let stack = stack.Push()
                    do! State.put stack
                    stack, Some result
                | Error msg -> 
                    Console.WriteLine msg
                    return! loop ()
        }
    loop ()
    
let requestInputWithMessage (message : Message) (parser : string -> Result<'a, string>) =
    Console.WriteLine message
    let mParser = fun s ->
        match parser s with
        | Ok result -> Ok result
        | Error msg ->
            let errMsg = { Type = message.Type ; Content = msg }
            Error (errMsg.ToString())
    requestInput mParser
    
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
    
/// Parse string as Chara list,
/// duplicates will be cut
let parseCharaList (input: string) =
    let strList = splitInputList input
    let someChara = strList |> List.map CharaType.Create
    
    let errors, successes = 
        someChara 
        |> List.fold (fun (errs, succs) result ->
            match result with
            | Error msg -> (msg :: errs, succs)
            | Ok value ->
                if succs |> List.exists (fun s -> s = value) then
                    (errs, succs)
                else
                    (errs, value :: succs)
            )
            ([], [])
    
    if List.isEmpty errors then
        Ok successes
    else
        Error (errors |> String.concat "; ")