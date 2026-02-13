namespace WereMF.State

open System
open WereMF.Common

type MainStatus =
    | InputPlayers
    | Roll
    | Game of GameState
    
type MainContext =
    {
        Rng : Random
        Players : Player list
        Roll : RollResult
    }
    
type MainState =
    {
        Status : MainStatus
        Context : MainContext
    }
    static member New seed =
        {
            Status = InputPlayers
            Context = { Rng = Random(seed) ; Players = []; Roll = RollResult.New() }
        }

type BindContext = MainContext * GameContext
type SkillContext = BindContext * NightContext