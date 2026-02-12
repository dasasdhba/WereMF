module WereMF.Skill.CaiMon

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.CaiMon

let updateReborn (double : bool) (state: EntityState) =
    let reborn =
        if double then { ReadyRound = 0 ; RebornRound = 2 } else
        match state.Reborn with
        | None -> { ReadyRound = 1 ; RebornRound = 1 }
        | Some _ -> { ReadyRound = 0 ; RebornRound = 2 }
    { state with Reborn = Some reborn }

type CaiMonSkill =
    {
        Double : bool
        Dead : bool
    }
    static member New () = { Double = false ; Dead = false }
    interface ISkill
    interface ISkillCanExecute with
        member this.CanExecute context sending =
            let target = sending |> getRealTarget
            target |> context.Game.HasEntity
            && target |> context.Game.GetEntity |> Entity.getState |> EntityState.isDead
    interface ISkillCost with
        member this.Cost sending = monad {
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            // 双弹簧默认 -1
            let target = if sending.Spring.IsSome then source else sending.Target
            let tEntity = target |> context.Game.GetEntity
            let cost = if tEntity.State |> EntityState.isDead then 2 else 1
            let entity = entity |> updateRoleWithHandler
                             (fun (f: CaiMonRole) -> { f with CaiCount = f.CaiCount - cost })
                             handler
            let context = { context with Game = context.Game.UpdateEntity entity }
            do! State.put context
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let remain = entity |> getFromRoleWithHandler
                            (fun f -> f.CaiCount)
                            handler
            let remain = defaultArg remain 0
            let skill, context =
                if remain > 0 then this, context else
                sendMessage { Type = ToPlayer entity.Player ; Content = "你的彩条用完了" }
                let state = context.Night.GetPlayerState source
                let state = { state with Blocked = true }
                let context = { context with Night = context.Night.SetPlayerState state }
                { this with Dead = true }, context
            do! State.put context
            if target |> isDoged context.Night then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想给{recv}发彩条，被Doge挡了"
                do! State.put { context with Night = night }
                skill
            else
                let tEntity = target |> context.Game.GetEntity
                let tEntity = { tEntity with State = updateReborn this.Double tEntity.State }
                let context = { context with Game = context.Game.UpdateEntity tEntity }
                let context =
                    if tEntity.State.Reborn.IsNone
                       || tEntity.State.Reborn.Value.Reborn |> not then context else
                    // 当晚复活
                    let tEntity = { tEntity with State.Dead.Dead = false }
                    let context = { context with Game = context.Game.UpdateEntity tEntity }
                    let handlers = getPendingHandlers tEntity.Player tEntity.Role
                    let ps = handlers |> List.map (fun h -> createPendingSkill h tEntity)
                    let night = { context.Night with PendingSkills = ps @ context.Night.PendingSkills }
                    sendMessage { Type = ToPlayer tEntity.Player ; Content = "你复活了" }
                    { context with Night = night }
                do! State.put context
                skill
        }
    interface ISkillSummary with
        member this.Priority = 9
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.Dead |> not then None else
            
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            Some {
                Target = entity
                Request = DeadRequest.New Force
            }
        }

// 解析彩条数量，格式: "玩家ID" 或 "玩家ID d"
let parseCaiMonInput (input: string) : Result<PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 ->
        parsePlayerId parts[0] |> Result.map (fun id -> (id, false))
    | 2 ->
        let isDouble = parts[1].ToLower() = "d"
        if not isDouble then Error "请输入 d 表示用两根彩条"
        else parsePlayerId parts[0] |> Result.map (fun id -> (id, true))
    | _ -> Error "请输入格式: 玩家编号 [d]"

// 彩怪技能发送
let caiMonSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let caiCount =
        match ps.Handler.GetFromEntity entity with
        | :? CaiMonRole as caiMon -> caiMon.CaiCount
        | _ -> 0
    
    let title = $"输入要复活的死亡玩家编号，在结尾输入 d 表示使用两根彩条（剩余 {caiCount} 根彩条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterAlive game
                >> filterExceptIndex ps.Source "不能给自己彩条"
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if caiCount <= 0 then filterDisabled "你没有彩条了" else id)
    let filter = giveUpOrFilterWith filter
    let def () =
        if caiCount <= 1 then { Double = false ; Dead = false } :> ISkill else
        let msg = { Type = ToPlayer entity.Player ; Content = "你可以选择用一根还是两根彩条（1：两根；0：一根）" }
        let yes = requestInputWithMessage msg parseBool
        { Double = yes ; Dead = false } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! target, double = parseCaiMonInput input
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        
        if (double && caiCount < 2) || (double |> not && caiCount < 1) then
            return! Error "彩条不足"
        else
            let skill = Skill.New ps target { Double = double; Dead = false }
            [ skill |> Some ]
       }
    
    ps |> sendSkillWith title filter parser def
