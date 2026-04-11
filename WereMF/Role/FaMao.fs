module WereMF.Role.FaMao

open FSharp.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Api

type FaMaoRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = FaMao
            Priority = 0
            SummaryName = FaMao.ToString ()
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
