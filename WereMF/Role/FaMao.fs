module WereMF.Role.FaMao

open WereMF.Common
open WereMF.Module.Role

type FaMaoRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = FaMao
            Priority = 0
            SummaryName = FaMao.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with FirstRound = true }