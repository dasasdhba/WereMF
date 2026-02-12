namespace WereMF.State

open WereMF.Common

type PlayerNightState =
    {
        Id : PlayerId
        Doge : PlayerId list
        Kirby : (PlayerId * RoleHandler) option
        Spring : bool
        Blocked : bool
    }
    static member New player =
        { Id = player ; Doge = [] ; Kirby = None ; Spring = false ; Blocked = false }

type NightContext =
    {
        PlayerStates : PlayerNightState list
        PendingSkills : PendingSkill list
        Skills : Skill list
        QueuedSkills: Skill list
        SummarySkills: Skill list
        Messages: string list
    }
    member this.GetPlayerPendingSkills player =
        this.PendingSkills |> List.filter (fun ps -> ps.Source = player)
    member this.GetPlayerState player =
        this.PlayerStates |> List.find (fun ps -> ps.Id = player)
    member this.SetPlayerState newState =
        let newStates = this.PlayerStates |> List.map (fun ps ->
            if ps.Id = newState.Id then newState else ps)
        { this with PlayerStates = newStates }
    member this.AddMessage message =
        { this with Messages = this.Messages @ [ message ] }
    member this.AddMessages messages =
        { this with Messages = this.Messages @ messages }
    static member New players =
        {
            PlayerStates = players |> List.map (fun p -> PlayerNightState.New p)
            PendingSkills = []
            Skills = []
            QueuedSkills = []
            SummarySkills = []
            Messages = []
        }

