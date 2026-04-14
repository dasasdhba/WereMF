namespace WereMF.State

open FSharp.Data
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
    member this.ToJsonValue () =
        JsonValue.Record [|
            "roll_pairs", this.Rolls
                          |> List.mapJson (fun r -> r.ToJsonValue())
            "leaf_charas", this.LeafRolls
                          |> List.mapJson (fun c -> c.ToJsonValue())
        |]
    static member New() = { Rolls = [] ; LeafRolls = [] }