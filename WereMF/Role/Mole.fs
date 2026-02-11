module WereMF.Role.Mole

open WereMF.Common
open WereMF.Module.Role

type MoleRole =
    {
        RedGround : bool
    }
    static member New () = { RedGround = false }
    interface IRole with
        member this.Base = {
            CharaType = Mole
            Priority = 0
            SummaryName = Mole.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with RedGround = false }