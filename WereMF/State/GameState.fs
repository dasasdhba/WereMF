module WereMF.State.GameState

open WereMF.Type.Entity
open WereMF.State.NightState

type GameStatus =
    | Start
    | Night of NightState
    | Day
    | End

type GameContext =
    {
        Entities : Entity list
    }
    member this.HasEntity p =
        this.Entities |> List.exists (fun e -> e.Player.Id = p)
    member this.TryGetEntity p =
        this.Entities |> List.tryFind (fun e -> e.Player.Id = p)
    member this.GetEntity p =
        this.Entities |> List.find (fun e -> e.Player.Id = p)

type GameState =
    {
        Status : GameStatus
        Context : GameContext
    }

let createGameState entities = {
    Status = Start
    Context = { Entities = entities }
}
