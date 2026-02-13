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
            let (main, game), night = context
            target |> game.HasEntity
            && target |> game.GetEntity |> Entity.getState |> EntityState.isDead
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = if sending.Spring.IsSome then source else sending.Target
            let cost = if this.Double then 2 else 1
            let entity = entity |> updateRoleWithHandler
                             (fun (f: CaiMonRole) -> { f with CaiCount = f.CaiCount - cost
                                                              RebornList = target :: f.RebornList })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let remain = entity |> getFromRoleWithHandler
                            (fun f -> f.CaiCount)
                            handler
            let skill, night =
                if remain > 0 then this, night else
                sendMessage { Type = ToPlayer entity.Player ; Content = "你的彩条用完了" }
                let state = night.GetPlayerState source
                let state = { state with Blocked = true }
                let night = night.SetPlayerState state
                { this with Dead = true }, night
            do! State.put ((main, game), night)
            if target |> isDoged night then
                sendMessage { Type = ToPlayer entity.Player ; Content = "失败" }
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想给{recv}发彩条，被Doge挡了"
                do! State.put ((main, game), night)
                skill
            else
                let tEntity = target |> game.GetEntity
                let tEntity = { tEntity with State = updateReborn this.Double tEntity.State }
                let game = game.UpdateEntity tEntity
                let game, night =
                    if tEntity.State.Reborn.IsNone
                       || tEntity.State.Reborn.Value.Reborn |> not then game, night else
                    let tEntity = { tEntity with State.Dead.Dead = false }
                    let game = game.UpdateEntity tEntity
                    let handlers = getPendingHandlers tEntity.Player tEntity.Role
                    let ps = handlers |> List.map (fun h -> createPendingSkill h tEntity)
                    let night = { night with PendingSkills = ps @ night.PendingSkills }
                    sendMessage { Type = ToPlayer tEntity.Player ; Content = "你复活了" }
                    game, night
                do! State.put ((main, game), night)
                skill
        }
    interface ISkillSummary with
        member this.Priority = 9
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.Dead |> not then None else

            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
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
    let caiCount, rebornList =
        match ps.Handler.GetFromEntity entity with
        | :? CaiMonRole as caiMon -> caiMon.CaiCount, caiMon.RebornList
        | _ -> 0, []
    
    let title = $"输入要复活的死亡玩家编号，在结尾输入 d 表示使用两根彩条（剩余 {caiCount} 根彩条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterAlive game
                >> filterExceptIndex ps.Source "你不能给自己彩条"
                >> filterExceptIndexList rebornList "你已经复活过这个玩家了"
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
