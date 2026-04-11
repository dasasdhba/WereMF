module WereMF.Role.Creeper

open WereMF.Module.Api
open FSharp.Data
open WereMF.Common

type CreeperRole =
    {
        BombCount : int
        PlacedList : PlayerId list
    }
    static member New () = { BombCount = 3 ; PlacedList = [] }
    interface IRole with
        member this.Base = {
            CharaType = Creeper
            Priority = 0
            SummaryName = Creeper.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "bomb_count", decimal this.BombCount |> JsonValue.Number
            "placed_list", this.PlacedList |> List.mapJson (fun p -> p.ToJsonValue())
        |]
