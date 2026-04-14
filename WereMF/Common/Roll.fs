namespace WereMF.State

open WereMF.Common
    
type RollPair =
    {
        PlayerId : PlayerId
        Type : CharaType
        Reset : bool
    }
    
type RollResult =
    {
        Rolls : RollPair list
        LeafRolls : CharaType list
    }
    member this.BoomCount =
        this.Rolls |> List.filter (fun r -> r.Type.GetCamp () = Boom) |> List.length
    static member New() = { Rolls = [] ; LeafRolls = [] }