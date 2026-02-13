module WereMF.Role.HeChong

open FSharpPlus
open FSharpPlus.Data
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
    member this.GetSubHandler () =
        let sub = createSubFunctor
                   (fun k -> k.CopiedRole.Value)
                   (fun v k -> { k with CopiedRole = Some v })
        sub |> CommonHandler
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = this.GetSubHandler ()
               sub.Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            if this.CopiedRole.IsNone then [] else
            let role = this.CopiedRole.Value
            getDayStartDeadRequest role
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
    interface IRolePreventDead with
        member this.Prevent dead= monad {
            if this.CopiedRole.IsNone then None else
            let role = this.CopiedRole.Value
            let! context = State.get
            let context, result = tryPreventDead dead context role
            if result.IsNone then None else
            let r = { this with CopiedRole = Some result.Value }
            do! State.put context
            r :> IRole |> Some
        }
    member private this.UpdateCopiedRoleAndContextWith func = monad {
        if this.CopiedRole.IsNone then this :> IRole else
        let role = this.CopiedRole.Value
        let! context = State.get
        let context, role = role |> func context
        do! State.put context
        { this with CopiedRole = Some role } :> IRole
    }
    interface IRoleUpdateOnVoteStart with
        member this.Update player =
            this.UpdateCopiedRoleAndContextWith (updateOnVoteStart player)
    interface IRoleUpdateOnVoteEnd with
        member this.Update entity game =
            this.UpdateCopiedRoleAndContextWith (updateOnVoteEnd entity game)
