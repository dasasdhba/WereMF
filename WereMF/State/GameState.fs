namespace WereMF.State

open WereMF.Common

type GameStatus =
    | Start
    | Night of NightContext
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
    member this.UpdateEntity e =
        let r = this.Entities |> List.map (fun e' -> if e'.Player.Id = e.Player.Id then e else e')
        { this with Entities = r }

type GameState =
    {
        Status : GameStatus
        Context : GameContext
    }
    static member New entities = {
        Status = Start
        Context = { Entities = entities }
    }
