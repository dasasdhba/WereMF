module WereMF.Role.XianSong

open FSharp.Data
open FSharpPlus
open WereMF.Common
open WereMF.Module.Role

type XianSongRole =
    {
        MfaList : PlayerId list
        CanReborn: bool
        Reborn : bool option
        Disabled : bool option
    }
    static member New () =
        { MfaList = [] ; CanReborn = false ;  Reborn = None ; Disabled = None }
    member this.IsDisabled () =
        this.Disabled.IsSome
    member this.IsRebornChoice () =
        this.Reborn.IsSome && this.Reborn.Value = true
    interface IRole with
        member this.Base = {
            CharaType = XianSong
            Priority = 1
            SummaryName = XianSong.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "mfa_list", this.MfaList |> List.mapJson (fun p -> p.ToJsonValue())
            "can_reborn", (this.Reborn.IsNone && this.CanReborn) |> JsonValue.Boolean
            "can_force_choice", this.IsRebornChoice () |> JsonValue.Boolean
            "disabled", this.IsDisabled () |> JsonValue.Boolean
        |]
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            { this with CanReborn = false }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = updateNightOptionBool this.Disabled
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with Disabled = None }
    interface IRolePreventDead with
        member this.Prevent dead = monad {
            if dead = Force || this.Reborn.IsSome || this.CanReborn |> not then None else
            let role = { this with Reborn = Some true } :> IRole
            Some { NewRole = role; StateSetter = id }
        }