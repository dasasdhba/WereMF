module WereMF.Role.HeChong

open WereMF.Common
open WereMF.Module.Role

type HeChongRole =
    {
        CopiedRole : IRole option
        LastSelected : SelectionState
    }
    static member New () = { CopiedRole = None ; LastSelected = SelectionState.New () }
    member private this.UpdateCopiedRoleWith updater =
        match this.CopiedRole with
        | Some role -> { this with CopiedRole = Some (role |> updater) }
        | None -> this
    interface IRole with
        member this.Base = {
            CharaType = HeChong
            Priority = 9
            SummaryName = HeChong.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
               (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with CopiedRole = None }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let r = this.UpdateCopiedRoleWith updateOnDayStart
            { r with LastSelected = r.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update dead =
            this.UpdateCopiedRoleWith (updateOnDead dead)
                
