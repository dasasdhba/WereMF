module WereMF.Role.Doctor

open FSharp.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Api

type DoctorRole =
    {
        Capsule : int
        Round: int
    }
    static member New () = { Capsule = 3; Round = 0 }
    interface IRole with
        member this.Base = {
            CharaType = Doctor
            Priority = 0
            SummaryName = Doctor.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "capsule", JsonValue.Number (decimal this.Capsule)
        |]
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            match this.Round with
            | v when v > 1 ->
                { this with Round = 1 ; Capsule = 3 }
            | v ->
                { this with Round = v + 1 }
