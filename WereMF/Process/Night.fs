module WereMF.Process.Night

open FSharpPlus
open FSharpPlus.Data
open WereMF.GameState
open WereMF.NightState

let nightInit (n: NightState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    return current, true
}

let nightAction (n: NightState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    return current, true
}

let nightSummary (n: NightState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    return current, true
}

let nightStep (n : NightState) : State<GameStack, GameStack * bool> = monad {
    let! current, result =
        match n.Status with
        | NightStatus.Init -> nightInit n
        | Action -> nightAction n
        | Summary -> nightSummary n
            
    do! State.put current
    return current, result
}