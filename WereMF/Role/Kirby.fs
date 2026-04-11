module WereMF.Role.Kirby

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Cli
open WereMF.Module.Api

type KirbyRole =
    {
        CopiedRole : IRole option
    }
    static member New () = { CopiedRole = None }
    member private this.UpdateCopiedRoleWith updater =
        match this.CopiedRole with
        | Some role -> { this with CopiedRole = Some (role |> updater) }
        | None -> this
    interface IRole with
        member this.Base = {
            CharaType = Kirby
            Priority = 9
            SummaryName = Kirby.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "copied_role", (
                match this.CopiedRole with
                | Some role ->
                    JsonValue.Record [|
                        "chara_type", (role |> getCharaType).ToJsonValue ()
                        "data", role.ToJsonValue ()
                    |]
                | None -> JsonValue.Null
            )
        |]
    member this.GetSubHandler () =
        let sub = createSubFunctor
                   (fun k -> k.CopiedRole.Value)
                   (fun v k -> { k with CopiedRole = Some v })
        sub |> KirbyHandler
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = this.GetSubHandler ()
               sub.Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRolePendingHandlers with
        member this.Get player =
            match this.CopiedRole with
            | Some role ->
                let chara = role |> getCharaType
                let msg = {
                    Type = ToPlayer player
                    Content = $"是否使用复制技能（{chara.ToString()}）？（1：使用；0：放弃并使用吸入技能）"
                    Api = ApiType.RequestKirbyUsingCopySkill
                    Data = JsonValue.Record [|
                        "chara_type", chara.ToJsonValue ()
                        "data", role.ToJsonValue ()
                    |]
                }

                let yes = requestInputWithMessage msg parseBool
                if yes |> not then [IdHandler] else

                let sub = this.GetSubHandler ()
                role |> getPendingHandlers player |> List.map (fun h -> sub.Bind h)
            | None -> [IdHandler]
    interface IRoleValidHandlers with
        member this.Get () =
            match this.CopiedRole with
                | Some role ->
                    let sub = this.GetSubHandler ()
                    let subList = role |> getValidHandlers |> List.map (fun h -> sub.Bind h)
                    IdHandler :: subList
                | None -> [IdHandler]
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightInit
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update dead =
            this.UpdateCopiedRoleWith (updateOnDead dead)
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            if this.CopiedRole.IsNone then [] else
            let role = this.CopiedRole.Value
            getNightStartDeadRequest role
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            if this.CopiedRole.IsNone then [] else
            let role = this.CopiedRole.Value
            getDayStartDeadRequest role
    interface IRolePreventDead with
        member this.Prevent dead= monad {
            if this.CopiedRole.IsNone then None else
            let role = this.CopiedRole.Value
            let! context = State.get
            let context, result = tryPreventDead dead context role
            if result.IsNone then None else
            let result = result.Value
            let r = { this with CopiedRole = Some result.NewRole }
            do! State.put context
            Some { result with NewRole = r }
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