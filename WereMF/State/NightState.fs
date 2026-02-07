module WereMF.State.NightState

open WereMF.Type.Skill
open WereMF.Type.Player

type PlayerNightState = {
    Player : PlayerId
    Doge : PlayerId option
    Kirby : PlayerId option
    Spring : bool
}

type NightContext =
    {
        PendingSkills : PendingSkill list
        PlayerStates : PlayerNightState list
    }
    member this.GetPlayerPendingSkills player =
        this.PendingSkills |> List.filter (fun ps -> ps.Source = player)
    member this.GetPlayerState player =
        this.PlayerStates |> List.find (fun ps -> ps.Player = player)
    member this.SetPlayerState player newState =
        let newStates = this.PlayerStates |> List.map (fun ps ->
            if ps.Player = player then newState else ps)
        { this with PlayerStates = newStates }

type NightStatus =
    | Start
    | Action
    | Summary
    
type NightState =
    {
        Status : NightStatus
        Context : NightContext
    }
    member this.SetStatus(status) =
        { this with Status = status }

let newNightContext = { PendingSkills = [] ; PlayerStates = [] }
let newNightState = { Status = Start; Context = newNightContext }

