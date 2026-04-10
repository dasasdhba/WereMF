module WereMF.Role.Mole

open FSharp.Data
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
        member this.ToJsonValue () =
            JsonValue.Record [|
                "red_ground", JsonValue.Boolean this.RedGround
                "ground_pool", this.Roll |> List.mapJson (fun i -> decimal i |> JsonValue.Number)
            |]
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            { this with RedGround = false }