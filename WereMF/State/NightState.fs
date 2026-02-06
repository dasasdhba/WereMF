module WereMF.NightState

open WereMF.Skill

type ActionData = {
    Priority : int
    Skill : Skill
}

type NightStatus =
    | Init
    | Action
    | Summary
    
type NightState =
    {
        Status : NightStatus
        Actions : ActionData list
    }
    member this.SetStatus(status) =
        { this with Status = status }
    member this.SetActions(actions) =
        { this with Actions = actions }
    member this.AddAction(action) =
        { this with Actions = action :: this.Actions }
        
let newNightState = { Status = Init; Actions = [] }

// -----------------------------------------------------------
// skill judge

