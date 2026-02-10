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
    member private this.UpdateRolesWith updater =
        { this with Roles = this.Roles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = Leaf
            Priority = 100
            SummaryName = this.SummaryName
        }
    interface IRoleQueriedHandler with
        member this.Get (random : Random) =
            let idx = if this.Fury then random.Next this.Roles.Length else 0
            let role = this.Roles[idx]
            let sub = createSubFunctor
                       (fun k -> k.Roles[idx])
                       (fun v k -> { k with Roles = k.Roles |> List.updateAt idx v })
            (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
    interface IRolePendingHandlers with
        member this.Get player = monad {
            if this.Fury |> not then
                let role = this.Roles[0]
                let! hs = role |> getPendingHandlers player
                let sub = createSubFunctor
                               (fun k -> k.Roles[0])
                               (fun v k ->
                     { k with Roles = k.Roles |> List.updateAt 0 v })
                hs |> List.map (fun h -> (sub |> CommonHandler).Bind h)
            else
                let mutable result = []
                for i = 1 to this.Roles.Length - 1 do
                    let role = this.Roles[i]
                    let! hs = role |> getPendingHandlers player
                    let sub = createSubFunctor
                                   (fun k -> k.Roles[i])
                                   (fun v k ->
                          { k with Roles = k.Roles |> List.updateAt i v })
                    result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
                result
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateRolesWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            this.UpdateRolesWith updateOnDead
