open FSharpPlus
open FSharpPlus.Data
open WereMF.GameState
open WereMF.Init
open WereMF.Roll
open WereMF.RollState

let rec mainLoop () : State<GameStack, unit> =
    monad {
        let! current = State.get
        match current.Status with
        | GameStatus.Init ->
            let! current, r = initPlayers ()
            do! State.put current
            if r then
                let current = current.SetStatus (Roll newRollState)
                do! State.put current
            do! mainLoop ()
        | Roll roll ->
            let! current, r = rollStep roll
            do! State.put current
            if r then
                // TODO: setting entities here
                let current = current.SetStatus Night
                do! State.put current
            do! mainLoop ()
        | _ -> return ()
    }

State.run (mainLoop ()) newGame |> ignore