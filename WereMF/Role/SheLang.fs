module WereMF.Role.SheLang

open FSharp.Data
open WereMF.Common
open WereMF.Module.Role

type SheLangRole =
    {
        LastSelected : SelectionState
    }
    static member New () = { LastSelected = SelectionState.New () }
    interface IRole with
        member this.Base = {
            CharaType = SheLang
            Priority = 4
            SummaryName = SheLang.ToString ()
        }
        member this.ToJsonValue () =
            JsonValue.Record [|
                "last_selected", this.LastSelected.ToJsonValue ()
            |]
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with LastSelected = SelectionState.New () }
