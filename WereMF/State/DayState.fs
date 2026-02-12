namespace WereMF.State

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