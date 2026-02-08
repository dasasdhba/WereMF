module WereMF.Update.Night

open FSharpPlus
open FSharpPlus.Data
open WereMF.Module.Cli
open WereMF.Module.Game
open WereMF.State
open WereMF.Module

let nightStart () = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let entities = game.Entities |> List.map (Entity.updateOnNightStart main)
    let game = { game with Entities = entities }
    sendMessage { Type = Public ; Content = "晚上开始\n" + (printNightSummary game) }
    do! State.put (main, game)
}

let nightUpdate (night : NightContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let _, (main, game) = State.run (nightStart ()) (main, game)
    do! State.put (main, game)
    Ok Day
}