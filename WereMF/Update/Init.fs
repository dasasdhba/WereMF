module WereMF.Update.Init

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Roll
open WereMF.State

/// input and init players
let initPlayers () =
    let parser = fun input ->
        let players = splitInputList input
        if players.Length < minPlayer then
            Error "玩家人数不足"
        elif players.Length > maxPlayer then
            Error "玩家人数过多"
        else
            Ok players
    monad {
        let! (main: MainContext) = State.get
        let inputMsg = {
            Type = Internal
            Content = $"输入玩家列表（{minPlayer}~{maxPlayer} 人）"
        }
        let result = requestInputWithMessage inputMsg parser
        match result with
        | Ok players ->
            let pList = [1..players.Length]
                        |> List.map (fun i -> { Id = PlayerId i ; Name = players[i - 1] })
            let main = { main with Players = pList }
            do! State.put main
            let pMessage = pList |> List.map (fun p -> p.ToInGameString() + "\n") |> List.reduce (+)
            sendMessage { Type = Public ; Content = $"\n{pMessage}" }
            Ok Roll
        | Error c -> Error c
    }