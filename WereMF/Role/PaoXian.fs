module WereMF.Role.PaoXian

open WereMF.Module.Api
open FSharp.Data
open WereMF.Common

type PaoXianRole =
    | PaoXianRole
    interface IRole with
        member this.Base = {
            CharaType = PaoXian
            Priority = 0
            SummaryName = PaoXian.ToString ()
        }
        member this.ToJsonValue() = JsonValue.Null

