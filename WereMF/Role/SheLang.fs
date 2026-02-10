module WereMF.Role.SheLang

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
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }