module WereMF.Role.ShiWu

open WereMF.Common
open WereMF.Module.Role

type ShiWuRole =
    {
        LastSelected : SelectionState
        Broadcasted : bool
        Exposed : bool
    }
    static member New () = { LastSelected = SelectionState.New () ; Broadcasted = false ; Exposed = false }
    interface IRole with
        member this.Base = {
            CharaType = ShiWu
            Priority = 7
            SummaryName = ShiWu.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Exposed = false }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update _ =
            { this with LastSelected = SelectionState.New () }

let exposeIfShiWu (handler: RoleHandler) entity =
    let role = handler.GetFromEntity entity
    match role with
    | :? ShiWuRole as shiWu ->
        let shiWu = { shiWu with Exposed = true }
        handler.SetToEntity shiWu entity
    | _ -> entity