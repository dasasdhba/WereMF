module WereMF.State.MainState

open System
open WereMF.State.GameState
open WereMF.Type.Player

type MainStatus =
    | WaitForPlayers
    | Roll
    | Game of GameState
    | End
    
type MainContext =
    {
        Rng : Random
        Players : Player list
    }
    
type MainState =
    {
        Status : MainStatus
        Context : MainContext
    }
    
let createMainState seed =
    {
        Status = WaitForPlayers
        Context = { Rng = Random(seed) ; Players = [] }
    }