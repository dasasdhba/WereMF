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
    interface IRoleQueriedHandler with
        member this.Get (random : Random) =
            let idx = if this.Fury then random.Next this.Roles.Length else 0
            let role = this.Roles[idx]
            let sub = this.GetSubHandler idx
            sub.Bind (role |> getQueriedHandler random)
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
    interface IRolePendingHandlers with
        member this.Get player =
            this.GetHandlersWith (getPendingHandlers player)
    interface IRoleValidHandlers with
        member this.Get () =
            this.GetHandlersWith getValidHandlers
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
        member this.Prevent dead handler = monad {
            let! context = State.get
            let min = if this.Fury then 1 else 0
            let max = if this.Fury then this.Roles.Length - 1 else 0
            let context, result =
                [min..max] |> List.fold (fun (c, r) i ->
                    if r then (c, r) else
                    let role = this.Roles[i]
                    let sub = this.GetSubHandler i
                    let subHandler = handler.Bind sub
                    tryPreventDead dead subHandler c role
                ) (context, false)
            do! State.put context
            result
        }