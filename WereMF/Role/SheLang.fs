module WereMF.Role.SheLang

open WereMF.Common
open WereMF.Module.Role

type SheLangRole =
    {
        LastSelected : SelectionState
        Disabled : bool option
    }
    static member New () = { LastSelected = SelectionState.New () ; Disabled = None }
    member this.IsDisabled ()
        = this.Disabled.IsSome
    interface IRole with
        member this.Base = {
            CharaType = SheLang
            Priority = 4
            SummaryName = SheLang.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with LastSelected = SelectionState.New () }
