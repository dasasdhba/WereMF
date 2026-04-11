module WereMF.Role.Myz

open WereMF.Module.Api
open FSharp.Data
open WereMF.Common

type MyzRole =
    {
        Revealed : bool
    }
    static member New () = { Revealed = false }
    interface IRole with
        member this.Base = {
            CharaType = Myz
            Priority = 11
            SummaryName = Myz.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "revealed", JsonValue.Boolean this.Revealed
        |]
