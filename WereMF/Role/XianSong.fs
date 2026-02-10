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
    interface IRole with
        member this.Base = {
            CharaType = XianSong
            Priority = 1
            SummaryName = XianSong.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = None }
