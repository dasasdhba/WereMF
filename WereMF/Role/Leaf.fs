module WereMF.Role.Leaf

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role

type LeafRole =
    {
        Roles : IRole list
        Fury : bool
    }
    static member New (roles : IRole list) = { Roles = roles ; Fury = false }
    member private this.SummaryName =
        let selects = this.Roles
                        |> List.map getSummaryName
                        |> String.concat " "
        $"{Leaf.ToString()}（{selects}）"
    member private this.UpdateRolesWith (updater: IRole -> IRole) =
        if this.Fury |> not then
            { this with Roles = this.Roles |> List.updateAt 0 (this.Roles[0] |> updater) }
        else
            { this with Roles = [0..(this.Roles.Length - 1)] |> List.map (fun i ->
                            if i = 0 then this.Roles[i] else this.Roles[i] |> updater
                ) }
    interface IRole with
        member this.Base = {
            CharaType = Leaf
            Priority = 100
            SummaryName = this.SummaryName
        }
    interface IRoleLeaf with
        member this.Fury =
            this.Fury
        member this.SetFury () =
            { this with Fury = true }
    member this.GetSubHandler idx =
        let sub = createSubFunctor
                           (fun k -> k.Roles[idx])
                           (fun v k ->
                 { k with Roles = k.Roles |> List.updateAt idx v })
        sub |> CommonHandler
    member private this.GetHandlersWith func =
        if this.Fury |> not then
            let role = this.Roles[0]
            let hs = role |> func
            let sub = this.GetSubHandler 0
            hs |> List.map (fun h -> sub.Bind h)
        else
            let mutable result = []
            for i = 1 to this.Roles.Length - 1 do
                let role = this.Roles[i]
                let hs = role |> func
                let sub = this.GetSubHandler i
                result <- result @ (hs |> List.map (fun h -> sub.Bind h))
            result
    member this.GetQueriedHandlers (random : Random) =
        this.GetHandlersWith (fun h -> [getQueriedHandler random h])
    interface IRoleQueriedHandler with
        member this.Get (random : Random) =
            this.GetQueriedHandlers random |> List.randomChoiceWith random
    interface IRolePendingHandlers with
        member this.Get player =
            this.GetHandlersWith (getPendingHandlers player)
    interface IRoleValidHandlers with
        member this.Get () =
            this.GetHandlersWith getValidHandlers
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            this.UpdateRolesWith updateOnNightInit
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateRolesWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update dead =
            this.UpdateRolesWith (updateOnDead dead)
    member private this.GetDeadRequestWith func =
        if this.Fury |> not then
            let role = this.Roles[0]
            role |> func
        else
            this.Roles[1..] |> List.map (fun role ->
                role |> func) |> List.concat
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            this.GetDeadRequestWith getNightStartDeadRequest
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            this.GetDeadRequestWith getDayStartDeadRequest
    interface IRolePreventDead with
        member this.Prevent dead = monad {
            let min = if this.Fury then 1 else 0
            let max = if this.Fury then this.Roles.Length - 1 else 0
            let! context = State.get
            let context, result, success =
                [min..max] |> List.fold (fun (c, root, success) i ->
                    if success then c, root, success else
                    let role, setter = root
                    let sub = role.Roles[i]
                    let c, r = tryPreventDead dead c sub
                    if r.IsNone then c, root, success else
                    let r = r.Value
                    let role = { role with Roles = role.Roles |> List.updateAt i r.NewRole }
                    let setter = setter >> r.StateSetter
                    c, (role, setter), true
                ) (context, (this, id), false)
            do! State.put context
            if success then
                let role, setter = result
                Some { NewRole = role; StateSetter = setter }
            else
                None
        }
    member private this.UpdateCopiedRolesAndContextWith func = monad {
        let! context = State.get
        let min = if this.Fury then 1 else 0
        let max = if this.Fury then this.Roles.Length - 1 else 0
        let context, role =
            [min..max] |> List.fold (fun (c, root) i ->
                let sub = this.Roles[i]
                let c, r = func c sub
                let root = { root with Roles = root.Roles |> List.updateAt i r }
                c, root
            ) (context, this)
        do! State.put context
        role :> IRole
    }
    interface IRoleUpdateOnVoteStart with
        member this.Update player =
            this.UpdateCopiedRolesAndContextWith (updateOnVoteStart player)
    interface IRoleUpdateOnVoteEnd with
        member this.Update entity game =
            this.UpdateCopiedRolesAndContextWith (updateOnVoteEnd entity game)