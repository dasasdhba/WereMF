namespace WereMF.State

open WereMF.Common

type PlayerNightState =
    {
        PlayerId : PlayerId
        Doge : PlayerId option
        Kirby : PlayerId option
        Spring : bool
    }
    static member New player =
        { PlayerId = player ; Doge = None ; Kirby = None ; Spring = false }

type NightContext =
    {
        PendingSkills : PendingSkill list
        PlayerStates : PlayerNightState list
    }
    member this.GetPlayerPendingSkills player =
        this.PendingSkills |> List.filter (fun ps -> ps.Source = player)
    member this.GetPlayerState player =
        this.PlayerStates |> List.find (fun ps -> ps.PlayerId = player)
    member this.SetPlayerState player newState =
        let newStates = this.PlayerStates |> List.map (fun ps ->
            if ps.PlayerId = player then newState else ps)
        { this with PlayerStates = newStates }
    static member New players =
        { PendingSkills = [] ; PlayerStates = players |> List.map (fun p -> PlayerNightState.New p) }

