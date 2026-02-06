open FSharpPlus
open FSharpPlus.Data
open WereMF.GameState
open WereMF.Init
open WereMF.NightState
open WereMF.Process.Night
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
                let entities = createEntities roll
                let current = current.SetEntities entities
                let current = current.SetStatus (Night newNightState)
                do! State.put current
            do! mainLoop ()
        | Night night ->
            let! current, r = nightStep night
            do! State.put current
            if r then
                // TODO: check game win
                let current = current.SetStatus Day
                do! State.put current
            do! mainLoop ()
        | _ -> return ()
    }

State.run (mainLoop ()) newGame |> ignore