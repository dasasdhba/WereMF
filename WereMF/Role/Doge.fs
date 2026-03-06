module WereMF.Role.Doge

open WereMF.Common
open WereMF.Module.Role

type DogeRole =
    {
        LastSelected : SelectionState
        SelfSelected : bool
    }
    static member New () = { LastSelected = SelectionState.New () ; SelfSelected = false }
    interface IRole with
        member this.Base = {
            CharaType = Doge
            Priority = 10
            SummaryName = Doge.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with LastSelected = SelectionState.New () }
