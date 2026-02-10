module WereMF.Role.FenXia

open FSharpPlus
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type FenXiaRole =
    {
        FenCount : int
        CopiedRoles : IRole list
        RebornRound : int option
    }
    static member New () = { FenCount = 3 ; CopiedRoles = [] ; RebornRound = None }
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRoles = this.CopiedRoles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = FenXia.ToString ()
        }
    member private this.GetHandlersWith func =
        let mutable result = [IdHandler]
        for i in 0..(this.CopiedRoles.Length - 1) do
            let role = this.CopiedRoles[i]
            let hs = role |> func
            let sub = createSubFunctor
                           (fun k -> k.CopiedRoles[i])
                           (fun v k ->
                 { k with CopiedRoles = k.CopiedRoles |> List.updateAt i v })
            result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
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
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDead
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            let requests = this.CopiedRoles |> List.map (fun role ->
                role |> getNightStartDeadRequest) |> List.concat
            if this.RebornRound.IsSome && this.RebornRound.Value <= 0 then
                DeadRequest.New Force :: requests
            else
                requests
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            let rec loop idx =
                if idx >= this.CopiedRoles.Length then None else
                let role = this.CopiedRoles[idx]
                let result = role |> tryPreventDead context dead entity
                match result with
                | Some r -> Some (r, idx)
                | None -> loop (idx + 1)
            let r = loop 0
            match r with
            | Some (r, idx) ->
                let role = { this with CopiedRoles = this.CopiedRoles |> List.updateAt idx r.NewRole }
                Some { r with NewRole = role }
            | None ->
                if dead = Force || this.RebornRound.IsSome then None else
                let msg = { Type = ToPlayer entity.Player ; Content = "用一根粉条复活吗？（1：是；0：否）" }
                let yes = requestInputWithMessage msg parseBool
                if yes |> not then None else
                Some {
                    NewContext = context
                    NewEntity = entity
                    NewRole = { this with RebornRound = Some 2 ; FenCount = this.FenCount - 1 }
                }
    

// 过滤：允许选择任何玩家，但根据生死状态决定粉条消耗
let filterFenXia (game: GameContext) (fenCount: int) = function
    | Ok playerId ->
        let entity = game.GetEntity playerId
        let isDead = entity.State |> EntityState.isDead
        let cost = if isDead then 2 else 1
        
        if cost > fenCount then
            if isDead then
                Error "你的粉条不足（需要 2 根）"
            else
                Error "你的粉条不足（需要 1 根）"
        else
            Ok playerId
    | value -> value

// 粉侠技能发送
let fenXiaSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let fenCount = 
        match ps.Handler.GetFromEntity entity with
        | :? FenXiaRole as fenXia -> fenXia.FenCount
        | _ -> 0
    
    let title = $"输入要获取技能的角色编号（剩余 {fenCount} 根粉条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterExceptIndex ps.Source "你不能给自己粉条"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterFenXia game fenCount
    let filter = giveUpOrFilterWith filter
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    
    ps |> sendSkillWith title filter parser
