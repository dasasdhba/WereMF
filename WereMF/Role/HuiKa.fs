module WereMF.Role.HuiKa

open FSharp.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Api

type HuiKaRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = HuiKa
            Priority = 8
            SummaryName = HuiKa.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "first_round", JsonValue.Boolean this.FirstRound
        |]
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with FirstRound = true }
