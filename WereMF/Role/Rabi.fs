module WereMF.Role.Rabi

open FSharp.Data
open WereMF.Common
open WereMF.Module.Role

type RabiRole =
    {
        Round : int
    }
    static member New () = { Round = 0 }
    interface IRole with
        member this.Base = {
            CharaType = Rabi
            Priority = 0
            SummaryName = Rabi.ToString ()
        }
        member this.ToJsonValue () =
            JsonValue.Record [|
                "round", decimal this.Round |> JsonValue.Number
            |]
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Round = this.Round + 1 }
