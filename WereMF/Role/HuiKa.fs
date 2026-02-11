module WereMF.Role.HuiKa

open WereMF.Common
open WereMF.Module.Role

type HuiKaRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = HuiKa
            Priority = 8
            SummaryName = HuiKa.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with FirstRound = true }
