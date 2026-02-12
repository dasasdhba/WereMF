module WereMF.Role.Leaf

open System
open FSharpPlus
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
    interface IRoleQueriedHandler with
        member this.Get (random : Random) =
            let idx = if this.Fury then random.Next this.Roles.Length else 0
            let role = this.Roles[idx]
            let sub = createSubFunctor
                       (fun k -> k.Roles[idx])
                       (fun v k -> { k with Roles = k.Roles |> List.updateAt idx v })
            (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
    member private this.GetHandlersWith func =
        if this.Fury |> not then
            let role = this.Roles[0]
            let hs = role |> func
            let sub = createSubFunctor
                           (fun k -> k.Roles[0])
                           (fun v k ->
                 { k with Roles = k.Roles |> List.updateAt 0 v })
            hs |> List.map (fun h -> (sub |> CommonHandler).Bind h)
        else
            let mutable result = []
            for i = 1 to this.Roles.Length - 1 do
                let role = this.Roles[i]
                let hs = role |> func
                let sub = createSubFunctor
                               (fun k -> k.Roles[i])
                               (fun v k ->
                      { k with Roles = k.Roles |> List.updateAt i v })
                result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
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
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            if this.Fury |> not then
                let role = this.Roles[0]
                role |> getNightStartDeadRequest
            else
                this.Roles[1..] |> List.map (fun role ->
                    role |> getNightStartDeadRequest) |> List.concat
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            if this.Fury |> not then
                let role = this.Roles[0]
                role |> getDayStartDeadRequest
            else
                this.Roles[1..] |> List.map (fun role ->
                    role |> getDayStartDeadRequest) |> List.concat
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            let rec loop idx =
                if idx >= this.Roles.Length then None else
                let role = this.Roles[idx]
                let result = role |> tryPreventDead context dead entity
                match result with
                | Some r -> Some (r, idx)
                | None -> loop (idx + 1)
            let r = if this.Fury then loop 1 else
                        this.Roles[0] |> tryPreventDead context dead entity
                                      |> Option.map (fun r -> r, 0)
            monad {
                let! r, idx = r
                let role = { this with Roles = this.Roles |> List.updateAt idx r.NewRole }
                { r with NewRole = role }
            }
