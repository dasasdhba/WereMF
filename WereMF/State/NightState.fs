namespace WereMF.State

open WereMF.Common

type PlayerNightState =
    {
        Id : PlayerId
        Doge : (PlayerId * RoleHandler) option
        Kirby : (PlayerId * RoleHandler) option
        Spring : bool
    }
    static member New player =
        { Id = player ; Doge = None ; Kirby = None ; Spring = false }

type NightContext =
    {
        PlayerStates : PlayerNightState list
        PendingSkills : PendingSkill list
        Skills : ISkill list
    }
    member this.GetPlayerPendingSkills player =
        this.PendingSkills |> List.filter (fun ps -> ps.Source = player)
    member this.GetPlayerState player =
        this.PlayerStates |> List.find (fun ps -> ps.Id = player)
    member this.SetPlayerState player newState =
        let newStates = this.PlayerStates |> List.map (fun ps ->
            if ps.Id = player then newState else ps)
        { this with PlayerStates = newStates }
    static member New players =
        {
            PlayerStates = players |> List.map (fun p -> PlayerNightState.New p)
            PendingSkills = []
            Skills = []
        }

