module WereMF.Role.XianSong

open WereMF.Common
open WereMF.Module.Role

type XianSongRole =
    {
        MfaList : PlayerId list
        Reborn : bool option
        Disabled : bool option
    }
    static member New () = { MfaList = [] ; Reborn = None ; Disabled = None }
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
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = updateNightOptionBool this.Disabled
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with Disabled = None }
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            if dead = Force || this.Reborn.IsSome || this.MfaList.Length = 0 then None else
            Some {
                NewContext = context
                NewEntity = entity
                NewRole = { this with Reborn = Some true }
            } 
