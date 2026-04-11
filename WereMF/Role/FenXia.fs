module WereMF.Role.FenXia

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Cli
open WereMF.Module.Api

type FenXiaRole =
    {
        FenCount : int
        CopiedRoles : IRole list
        RebornRound : int option
    }
    static member New () = { FenCount = 3 ; CopiedRoles = [] ; RebornRound = None }
    member private this.SummaryName =
        if this.CopiedRoles.Length = 0 then FenXia.ToString() else
        let selects = this.CopiedRoles
                        |> List.map getSummaryName
                        |> String.concat " "
        $"{FenXia.ToString()}（{selects}）"
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRoles = this.CopiedRoles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = this.SummaryName
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "fen_count", decimal this.FenCount |> JsonValue.Number
            "copied_roles", (this.CopiedRoles |> List.mapJson (fun r ->
                JsonValue.Record [|
                    "chara_type", (r |> getCharaType).ToJsonValue ()
                    "data", r.ToJsonValue ()
                |]
            ))
        |]
    member this.GetSubHandler idx =
        let sub = createSubFunctor
                           (fun k -> k.CopiedRoles[idx])
                           (fun v k ->
                 { k with CopiedRoles = k.CopiedRoles |> List.updateAt idx v })
        sub |> CommonHandler
    member private this.GetHandlersWith func =
        let mutable result = [IdHandler]
        for i in 0..(this.CopiedRoles.Length - 1) do
            let role = this.CopiedRoles[i]
            let hs = role |> func
            let sub = this.GetSubHandler i
            result <- result @ (hs |> List.map (fun h -> sub.Bind h))
        result
    interface IRolePendingHandlers with
        member this.Get player =
            this.GetHandlersWith (getPendingHandlers player)
    interface IRoleValidHandlers with
        member this.Get () =
            this.GetHandlersWith getValidHandlers
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnNightInit
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            let r = this.UpdateCopiedRolesWith updateOnNightStart
            match r.RebornRound with
            | None -> r
            | Some v -> { r with RebornRound = Some (v - 1) }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update dead =
            this.UpdateCopiedRolesWith (updateOnDead dead)
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            let skills =
                let requests = this.CopiedRoles |> List.map (fun role ->
                    role |> getNightStartDeadRequest) |> List.concat
                if this.RebornRound.IsSome && this.RebornRound.Value <= 0 then
                    DeadRequest.New Force :: requests
                else
                    requests
            if this.FenCount <= 0 then DeadRequest.New Force :: skills else skills
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            let skills = this.CopiedRoles |> List.map (fun role ->
                    role |> getNightStartDeadRequest) |> List.concat
            if this.FenCount <= 0 then DeadRequest.New Force :: skills else skills
    interface IRolePreventDead with
        member this.Prevent dead = monad {
            let! context = State.get
            let context, result, success =
                [0..(this.CopiedRoles.Length-1)] |> List.fold (fun (c, root, success) i ->
                    if success then c, root, success else
                    let role, setter = root
                    let sub = role.CopiedRoles[i]
                    let c, r = tryPreventDead dead c sub
                    if r.IsNone then c, root, success else
                    let r = r.Value
                    let role = { role with CopiedRoles = role.CopiedRoles |> List.updateAt i r.NewRole }
                    let setter = setter >> r.StateSetter
                    c, (role, setter), true
                ) (context, (this, id), false)
            do! State.put context
            if success then
                let role, setter = result
                Some { NewRole = role; StateSetter = setter }
            else
            
            if dead = Force || this.RebornRound.IsSome || this.FenCount <= 1 then None else
            let entity, bind = context
            let msg = { Type = ToPlayer entity.Player ; Content = "用一根粉条复活吗？（1：是；0：否）" }
            let yes = requestInputWithRawMessage msg ApiType.RequestFenxiaReborn parseBool
            if yes |> not then None else
            
            let role = { this with RebornRound = Some 1 ; FenCount = this.FenCount - 1 }
            Some { NewRole = role; StateSetter = id }
        }
    member private this.UpdateCopiedRolesAndContextWith func = monad {
        let! context = State.get
        let context, role =
            [0..(this.CopiedRoles.Length-1)] |> List.fold (fun (c, root) i ->
                let sub = this.CopiedRoles[i]
                let c, r = func c sub
                let root = { root with CopiedRoles = root.CopiedRoles |> List.updateAt i r }
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