module WereMF.Update.Init

open FSharpPlus
open FSharpPlus.Data
open WereMF.State.MainState
open WereMF.Type.Player
open WereMF.Game.Cli
open WereMF.State.RollState

/// input and init players
let initPlayers () =
    let parser = fun input ->
        let players = splitInputList input
        if players.Length < MinPlayer then
            Error "玩家人数不足"
        elif players.Length > MaxPlayer then
            Error "玩家人数过多"
        else
            Ok players
    monad {
        let! main = State.get
        let inputMsg = {
            Type = Internal
            Content = $"输入玩家列表（{MinPlayer}~{MaxPlayer} 人）"
        }
        let result = requestInputWithMessage inputMsg parser
        match result with
        | Ok players ->
            let pList = [1..players.Length]
                        |> List.map (fun i -> { Id = PlayerId i ; Name = players[i - 1] })
            let main = { main with Players = pList }
            do! State.put main
            let pMessage = pList |> List.map (fun p -> p.ToAliveString() + "\n") |> List.reduce (+)
            sendMessage { Type = Public ; Content = $"\n{pMessage}" }
            Ok Roll
        | Error c -> Error c
    }