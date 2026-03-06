module WereMF.Role.Mole

open WereMF.Common
open WereMF.Module.Role

// 0: 花岗岩；1：土地；2：红土地
let private moleRollDefault : int list = [ 0; 0; 1; 1; 1; 2 ]

type MoleRole =
    {
        RedGround : bool
        Roll : int list
    }
    static member New () = { RedGround = false; Roll = moleRollDefault }
    interface IRole with
        member this.Base = {
            CharaType = Mole
            Priority = 0
            SummaryName = Mole.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with RedGround = false }