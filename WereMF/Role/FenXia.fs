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
        RebornRound : int
    }
    static member New () = { FenCount = 3 ; CopiedRoles = [] ; RebornRound = 0 }
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRoles = this.CopiedRoles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = FenXia.ToString ()
        }
    interface IRolePendingHandlers with
        member this.Get player = monad {
            let mutable result = [IdHandler]
            for i in 0..(this.CopiedRoles.Length - 1) do
                let role = this.CopiedRoles[i]
                let! hs = role |> getPendingHandlers player
                let sub = createSubFunctor
                               (fun k -> k.CopiedRoles[i])
                               (fun v k ->
                     { k with CopiedRoles = k.CopiedRoles |> List.updateAt i v })
                result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
            result
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            let r = this.UpdateCopiedRolesWith updateOnNightStart
            if r.RebornRound > 0 then { r with RebornRound = r.RebornRound - 1 }
            else r
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDead

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
