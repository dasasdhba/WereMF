module WereMF.Init

open FSharpPlus
open FSharpPlus.Data
open WereMF.Player
open WereMF.Cli
open WereMF.RollState

/// input and init players,
/// returns true if success
let initPlayers () =
    let parser = fun input ->
        let players = splitInputList input
        if players.Length < MinPlayer then
            Error "Not enough players"
        elif players.Length > MaxPlayer then
            Error "Too many players"
        else
            Ok players
    monad {
        let inputMsg = {
            Type = Internal
            Content = $"输入玩家列表（{MinPlayer}~{MaxPlayer} 人）"
        }
        let! current, result = requestInputWithMessage inputMsg parser
        do! State.put current
        match result with
        | Some players ->
            let pList = [1..players.Length]
                        |> List.map (fun i -> { Id = PlayerId i ; Name = players[i - 1] })
            let current = current.SetPlayers pList
            do! State.put current
            let pMessage = pList |> List.map (fun p -> p.ToAliveString() + "\n") |> List.reduce (+)
            sendMessage { Type = Public ; Content = $"\n{pMessage}" }
            return current, true
        | None ->
            return current, false
    }