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
    let result = monad {
        let! psList = createPendingSkills (game.Entities |> List.filter (
            fun e -> e |> Entity.getState |> EntityState.isDead |> not))
        let night = { night with PendingSkills = psList }
        // TODO: sending logic, should order by priority
        let mutable m, g, n = main, game, night
        for ps in psList do
            let r, (game, night) = State.run (sendSkill g ps) (g, n)
            let! _ = r
            g <- game
            n <- night
        m, g, n
    }
    match result with
    | Error e -> Error e
    | Ok (main, game, night) ->
        do! State.put (main, game)
        Ok night
}

let nightUpdate (night : NightContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let _, (main, game) = State.run (nightStart ()) (main, game)
    let next = monad {
        let rn, (main, game) = State.run (nightAction night) (main, game)
        let! night = rn
        main, game
    }
    match next with
    | Error e -> Error e
    | Ok (main, game) ->
        do! State.put (main, game)
        // TODO: maybe game win
        Ok Day
}