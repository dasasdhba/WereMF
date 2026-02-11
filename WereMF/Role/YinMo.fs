module WereMF.Role.YinMo

open WereMF.Common
open WereMF.Module.Role

type YinMoRole =
    {
        DiscCount : int
        Disabled : bool option
    }
    static member New count = { DiscCount = count ; Disabled = None }
    member this.IsDisabled ()
        = this.Disabled.IsSome
    interface IRole with
        member this.Base = {
            CharaType = YinMo
            Priority = 2
            SummaryName = YinMo.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = updateNightOptionBool this.Disabled
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with Disabled = None }
