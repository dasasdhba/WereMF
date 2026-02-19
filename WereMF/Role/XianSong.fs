module WereMF.Role.XianSong

open FSharpPlus
open FSharpPlus.Data
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
            { this with Reborn = Some true } :> IRole |> Some
        }