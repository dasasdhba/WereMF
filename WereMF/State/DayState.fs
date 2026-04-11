namespace WereMF.State

open FSharp.Data
open WereMF.Common

type PlayerVote =
    {
        Id : PlayerId
        Target : PlayerId option
        Confirmed : bool
    }
    member this.GetTarget () =
        defaultArg this.Target (PlayerId 0)
    static member New id =
        { Id = id; Target = None; Confirmed = false }
    member this.ToJsonValue () = JsonValue.Record [|
        "id", this.Id.ToJsonValue ()
        "target", match this.Target with | None -> JsonValue.Null | Some t -> t.ToJsonValue ()
        "confirmed", JsonValue.Boolean this.Confirmed
    |]

type DayContext =
    {
        Votes : PlayerVote list
    }
    member this.GetPlayerVote id =
        this.Votes |> List.find (fun ps -> ps.Id = id)
    member this.SetPlayerVote vote =
        { this with Votes = this.Votes |> List.map (fun v -> if v.Id = vote.Id then vote else v) }
    static member New players =
        {
            Votes = players |> List.map (fun p -> PlayerVote.New p)
        }
    member this.ToJsonValue () =
        this.Votes |> List.mapJson (fun v -> v.ToJsonValue ())