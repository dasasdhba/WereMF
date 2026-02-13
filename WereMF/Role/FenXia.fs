module WereMF.Role.FenXia

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Cli

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
        member this.Prevent dead handler = monad {
            let! context = State.get
            let context, result =
                [0..(this.CopiedRoles.Length-1)] |> List.fold (fun (c, r) i ->
                    if r then (c, r) else
                    let role = this.CopiedRoles[i]
                    let sub = this.GetSubHandler i
                    let subHandler = handler.Bind sub
                    tryPreventDead dead subHandler c role
                ) (context, false)
            do! State.put context
            if result then true else
            if dead = Force || this.RebornRound.IsSome || this.FenCount <= 1 then false else
            let entity, bind = context
            let msg = { Type = ToPlayer entity.Player ; Content = "用一根粉条复活吗？（1：是；0：否）" }
            let yes = requestInputWithMessage msg parseBool
            if yes |> not then false else
            
            let r = { this with RebornRound = Some 2 ; FenCount = this.FenCount - 1 }
            let entity = entity |> handler.SetToEntity r
            let main, game = bind
            let game = game.UpdateEntity entity
            let bind = main, game
            do! State.put (entity, bind)
            
            true
        }