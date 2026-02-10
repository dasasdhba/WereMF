module WereMF.Update.Night

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Role
open WereMF.Module.Utils
open WereMF.State
open WereMF.Module

let nightStart () = monad {
    let! (main: MainContext, game : GameContext) = State.get
    sendMessage { Type = Public ; Content = "晚上开始\n" + (printNightSummary game.Entities) }
    
    let rec updateNightDead idx (entities: Entity list) c =
        if idx >= entities.Length then c, entities else
        let (e: Entity) = entities[idx]
        let c, e = e |> Entity.updateOnNightStartRequestDead c
        let entities = entities |> List.updateAt idx e
        updateNightDead (idx + 1) entities c
    
    let context = RoleContext.Create main game
    let entities = game.Entities
    let context, entities = updateNightDead 0 entities context
    let main, game = context.Get ()
    let entities = entities |> List.map (Entity.updateOnNightStart main)
    let game = { game with Entities = entities }
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