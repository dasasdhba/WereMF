module WereMF.Update.Night

open FSharpPlus
open FSharpPlus.Data
open WereMF.Module.Cli
open WereMF.Module.Utils
open WereMF.Role.Bind
open WereMF.State
open WereMF.Module

let nightStart () = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let entities = game.Entities |> List.map (Entity.updateOnNightStart main)
    let game = { game with Entities = entities }
    sendMessage { Type = Public ; Content = "晚上开始\n" + (printNightSummary game.Entities) }
    do! State.put (main, game)
}

let nightAction night = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let psList = createPendingSkills (game.Entities |> List.filter (
        fun e -> e |> Entity.getState |> EntityState.isDead |> not))
    let night = { night with PendingSkills = psList }
    // TODO: sending logic, should order by priority
    do! State.put (main, game)
    night
}

let nightUpdate (night : NightContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let _, (main, game) = State.run (nightStart ()) (main, game)
    let night, (main, game) = State.run (nightAction night) (main, game)
    do! State.put (main, game)
    // TODO: maybe game win
    Day
}