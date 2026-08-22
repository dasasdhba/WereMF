module WereMF.Update.Day

open System
open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.EntityState
open WereMF.Module.Skill
open WereMF.Module.Api
open WereMF.State
open WereMF.Update.Night

let dayStart (day: DayContext) = monad {
    let! (main: MainContext, game : GameContext) = State.get
    sendRawMessage { Type = Public ; Content = "白天开始" } ApiType.DayStartBroadcast
    
    let main, game =
        [0..(game.Entities.Length - 1)] |> List.fold (fun (m, g) i ->
            let e = g.Entities[i]
            let e, (m, g) = updateOnDayStartRequestDead (e, (m, g))
            m, g
        ) (main, game)
    
    let entities = game.Entities |> List.map Entity.updateOnDayStart
    let game = { game with Entities = entities }
    sendMessage {
        Type = Public
        Content = "\n" + (printNightSummary entities)
        Api = ApiType.GameUpdateDay
        Data = game.ToJsonValue ()
    }
    
    do! State.put (main, game)
    day
}

type private VoteType =
    | VoteNormal of PlayerId * PlayerId
    | Suicide of PlayerId
    | Finish

let private parseVote (game: GameContext) (day: DayContext) (input: string) : Result<VoteType, string> = monad {
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 when parts[0] = "0" -> Finish
    | 2 ->
        let! x = parts[0] |> parsePlayerId
        if game.HasEntity x |> not then
            return! Error "玩家不存在"
        else
        
        let xEntity = game.GetEntity x
        if xEntity |> Entity.isDayBlocked then
            return! Error $"玩家{xEntity.Player.Name}无法行动"
        else
        
        let! x = Ok x |> voteSourceFilter game

        let y = parts[1]
        if y.ToLower() = "b" then
            if xEntity |> Entity.getValidCharaTypes |> List.contains JiaoHua then
                Suicide x
            else
                return! Error $"玩家{xEntity.Player.ToCliString ()}不是脚滑人"
        else
        
        let state = day.GetPlayerVote x
        if state.Confirmed then
            return! Error $"玩家{xEntity.Player.ToCliString ()}不能改票了"
        else
            
        let! y = y |> parsePlayerId |> voteTargetFilter x game
        VoteNormal (x, y)
    | _ ->
        return! Error "未知格式"
}

let private getValidVoteChoice (game: GameContext) (day: DayContext) =
    game.Entities |> List.map (fun e ->
        JsonValue.Record [|
            "id", e.Player.Id.ToJsonValue ()
            "can_vote", (
                    e |> isDayBlocked |> not && (e.Player.Id |> day.GetPlayerVote).Confirmed |> not
                    && Ok e.Player.Id |> (voteSourceFilter game) |> Result.isOk
                ) |> JsonValue.Boolean
            "can_suicide", (e |> Entity.getValidCharaTypes |> List.contains JiaoHua) |> JsonValue.Boolean
            "invalid_vote", createInvalidChoiceArray (voteTargetFilter e.Player.Id game) game.Entities
        |]
        ) |> List.toArray |> JsonValue.Array

let updateVote (x: PlayerId) (y: PlayerId) (day: DayContext) =
    let vote = day.GetPlayerVote x
    let vote =
        match vote.Target with
        | Some _ -> { vote with Target = Some y ; Confirmed = true }
        | None -> { vote with Target = Some y }
    day.SetPlayerVote vote

let canAnyoneVote (game: GameContext) (day: DayContext) =
    day.Votes |> List.exists (fun v ->
        Ok v.Id |> voteSourceFilter game |> Result.isOk &&
        game.GetEntity v.Id |> isDayBlocked |> not &&
        v.Confirmed |> not &&
        day.Votes |> List.exists (fun t -> Ok t.Id |> voteTargetFilter v.Id game |> Result.isOk))

let private getVoteString (vote: PlayerVote) =
    match vote.Target with
    | None -> ""
    | Some v when v = PlayerId 0 && vote.Confirmed -> "：弃票√"
    | Some v when v = PlayerId 0 -> "：弃票"
    | Some v when vote.Confirmed -> $"：{v}√"
    | Some v -> $"：{v}"

let private printVoteStartSummary (game: GameContext) (day: DayContext) =
    game.Entities |> printSummaryWith (fun e ->
        if e.State |> EntityState.isDead then e |> getNightSummary else
        let vote = day.GetPlayerVote e.Player.Id
        (e |> getNightSummary) + getVoteString vote
    )

let printVoteSummary (game: GameContext) (day: DayContext) =
    game.Entities |> printSummaryWith (fun e ->
        if e.State |> EntityState.isDead then e |> getDaySummary else
        let vote = day.GetPlayerVote e.Player.Id
        (e |> getDaySummary) + getVoteString vote
    )
    
type JiaoHuaAction =
    | Blocked
    | Protected

let private parseJiaoHuaInput (source: PlayerId) (game: GameContext) (input: string) : Result<PlayerId * JiaoHuaAction, string> = monad {
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 when parts[0] = "0" -> PlayerId 0, Blocked
    | 2 ->
        let! playerId = parsePlayerId parts[0]
        let! playerId = Ok playerId |> (voteTargetFilter source game)
        let! action =
            match parts[1].ToLower() with
            | "x" -> Ok Blocked
            | "p" -> Ok Protected
            | _ -> Error "（x=封住行动，p=保护玩家）"
        
        playerId, action
    | _ -> return! Error "请输入格式: 玩家编号 行动类型(x/p)"
}

let private updateIfJiaoHuaOut (entity : Entity) (game: GameContext) =
    if entity.State |> EntityState.isDead |> not then game else
    if entity |> Entity.getValidCharaTypes |> List.contains JiaoHua |> not then game else
    
    sendRawMessage { Type = Public
                     Content = $"{entity.Player.Name}可以取消一人下一个晚上的一次行动，或令一人不可被其他人的技能选中" }
                  ApiType.JiaohuaDeadSkillBroadcast
    let msg = {
        Type = ToPlayer entity.Player
        Content = "输入玩家编号和行动类型（x=封住行动，p=保护玩家），输入 0 放弃"
        Api = ApiType.RequestJiaohuaDeadSkill
        Data = game.Entities |> createInvalidChoiceArray (voteTargetFilter entity.Player.Id game)
    }
    let target, action = requestInputWithMessage msg (parseJiaoHuaInput entity.Player.Id game)
    if target <= PlayerId 0 then game else
    
    let tEntity = game.GetEntity target
    match action with
    | Blocked ->
        let tEntity = { tEntity with State.JiaoHuaBlocked = tEntity.State.JiaoHuaBlocked + 1 }
        sendRawMessage { Type = Public
                         Content = $"{tEntity.Player.Name}被已死亡的{entity.Player.Name}封住下一晚的一次行动" }
                       ApiType.JiaohuaDeadSkillBroadcast
        game.UpdateEntity tEntity
    | Protected ->
        let tEntity = { tEntity with State.JiaoHuaProtected = true }
        sendRawMessage { Type = Public
                         Content = $"{tEntity.Player.Name}被已死亡的{entity.Player.Name}保护" }
                       ApiType.JiaohuaDeadSkillBroadcast
        game.UpdateEntity tEntity
        
let private updateIfShiWuOut (entity : Entity) (bind: BindContext) =
    if entity.State |> EntityState.isDead |> not then bind else
    
    let rec update (kids: PlayerId list) (r: BindContext) =
        if kids.Length = 0 then r else
        let m, g = r
        let k = g.GetEntity kids.Head
        sendRawMessage { Type = Public
                         Content = $"由于{entity.Player.Name}绑架了{k.Player.Name}" }
                        ApiType.ShiwuInvolveBroadcast
        let request = DeadRequest.New Kill
        let k, (m, g) = requestDead request (k, r)
        let g = updateIfJiaoHuaOut k g
        let r = m, g
        update kids.Tail r
    let main, game = bind
    let kidnapped = game.Entities |> List.filter (fun e ->
        e.State.Kidnapped |> List.contains entity.Player.Id) |> List.map (fun e -> e.Player.Id)
    update kidnapped bind

let private updateIfVoteOut (entity : Entity) (bind: BindContext) =
    let main, game = bind
    let game = updateIfJiaoHuaOut entity game
    updateIfShiWuOut entity (main, game)
    
let requestVoteOut (entity : Entity) (bind: BindContext) =
    let request = DeadRequest.New Vote
    let entity, bind = requestDead request (entity, bind)
    updateIfVoteOut entity bind
    
let VoteOutIfTooManyGiveUp (day: DayContext) (bind: BindContext) =
    let main, game = bind
    let alive = game.Entities |> List.filter (fun e ->
        e.State |> EntityState.isDead |> not)
    let half = (float alive.Length) / 2.0 |> Math.Ceiling |> int
    let giveUp = day.Votes |> List.filter (fun v ->
        alive |> List.exists (fun q -> q.Player.Id = v.Id )
        && v.GetTarget () = PlayerId 0)
    if giveUp.Length < half then bind else
    
     sendRawMessage { Type = Public; Content = "半数玩家都弃票了！度娘要抽贴了！" } ApiType.BaiduBroadcast
     let r = giveUp |> List.randomChoiceWith main.Rng
     let rEntity = game.GetEntity r.Id
     sendRawMessage { Type = Public; Content = $"{rEntity.Player.Name}被抽贴了！" } ApiType.BaiduResultBroadcast
     requestVoteOut rEntity bind

let private getVoteOutPlayerWith (adder: PlayerVote -> int) (day : DayContext)=
    let votes = day.Votes |> List.map (fun v ->
            v, adder v
        )
    let results = day.Votes |> List.map (fun v ->
        v, votes |> List.filter (fun (u, a) -> u.GetTarget () = v.Id) |> List.sumBy (fun (u, a) -> a)
    )
    let _, max = results |> List.maxBy (fun (e, v) -> v)
    let voted = results |> List.filter (fun (e, v) -> v = max)
    if max = 0 || voted.Length > 1 then PlayerId 0 else
    let r, _ = voted.Head
    r.Id

let private getVoteOutPlayerNormal (day : DayContext)=
    let adder _ = 1
    getVoteOutPlayerWith adder day

let private getVoteOutPlayerWithBarLeader (day : DayContext) (game: GameContext) =
    let adder v =
        let e = game.GetEntity v.Id
        if e.State |> EntityState.hasBarVote then 2 else 1
    getVoteOutPlayerWith adder day
    
let getVoteOutPlayer (day : DayContext) (game: GameContext) =
    let p1 = getVoteOutPlayerNormal day
    let p2 = getVoteOutPlayerWithBarLeader day game
    if p1 = p2 then p1, game, None else
    let bar = game.Entities |> List.find (fun e -> e.State |> EntityState.hasBarVote)
    let bar = { bar with State = bar.State |> EntityState.clearBarVote }
    let game = game.UpdateEntity bar
    p2, game, Some bar.Player.Id

let private updateContextWith func game day =
    [0..(game.Entities.Length - 1)] |> List.fold (fun (g, d) i ->
            let e = g.Entities[i]
            func e g d
        ) (game, day)

let private updateContextOnVoteStart game day =
    updateContextWith updateOnVoteStart game day

let private updateContextOnVoteEnd game day =
    updateContextWith updateOnVoteEnd game day

let dayVote (day : DayContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    
    sendRawMessage { Type = Public ; Content = "投票开始" } ApiType.VoteStartBroadcast
    let game, day = updateContextOnVoteStart game day
    sendMessage {
        Type = Public
        Content = "\n" + (printVoteStartSummary game day)
        Api = ApiType.GameUpdateVote
        Data = day.ToJsonValue ()
    }
    
    let rec voteRec d =
        let msg = {
            Type = Internal
            Content = "输入 x y 表示 x 给 y 投票，若 x 是脚滑人，可以输入 x b 自爆；输入 0 结束投票环节"
            Api = ApiType.RequestVote
            Data = getValidVoteChoice game d
        }
        let vote = requestInputWithMessage msg (parseVote game d)
        match vote with
        | VoteNormal (x, y) ->
            let d = updateVote x y d
            sendMessage {
                Type = Public
                Content = $"\n{printVoteSummary game d}"
                Api = ApiType.GameUpdateVote
                Data = d.ToJsonValue ()
            }
            if canAnyoneVote game d then
                voteRec d
            else
                Finish, d
        | v -> v, d
    
    let result, day = voteRec day
    sendRawMessage { Type = Public ; Content = "投票结束" } ApiType.VoteEndBroadcast
    match result with
    | Suicide x ->
        let xEntity = game.GetEntity x
        sendRawMessage { Type = Public ; Content = $"{xEntity.Player.Name}自爆了" } ApiType.JiaohuaSuicideBroadcast
        let main, game = requestVoteOut xEntity (main, game)
        let entities = game.Entities |> List.map (fun e ->
                { e with State.Bomb = e.State.QueuedBomb ; State.QueuedBomb = 0 }
             )
        let game = { game with Entities = entities }
        do! State.put (main, game)
    | _ ->
        sendMessage {
            Type = Public
            Content = $"\n{printVoteSummary game day}"
            Api = ApiType.GameUpdateVote
            Data = day.ToJsonValue ()
        }
        let game, day = updateContextOnVoteEnd game day
        let main, game = VoteOutIfTooManyGiveUp day (main, game)
        let out, game, bar = getVoteOutPlayer day game
        if out <= PlayerId 0 then
            sendRawMessage { Type = Public ; Content = "投票结果是" } ApiType.VoteResultBroadcast
            sendRawMessage { Type = Public ; Content = "平票" } ApiType.VoteTieBroadcast
            do! State.put (main, game)
        else
        
        let o = game.GetEntity out
        if o.State |> isDead then
            sendRawMessage { Type = Public ; Content = $"由于{o.Player.Name}被抽帖，本轮投票没有其他玩家出局" } ApiType.VoteBaiduFailBroadcast
            match bar with
            | None ->
                do! State.put (main, game)
            | Some barId ->
                let b = game.GetEntity barId
                let b = { b with State.BarLeader = Some true }
                let game = game.UpdateEntity b
                do! State.put (main, game)
        else
            sendRawMessage { Type = Public ; Content = "投票结果是" } ApiType.VoteResultBroadcast
            let main, game = requestVoteOut o (main, game)
            do! State.put (main, game)
    ()
}

let dayUpdate (day: DayContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let day, (main, game) = State.run (dayStart day) (main, game)
    let _, (main, game) = State.run (dayVote day) (main, game)
    do! State.put (main, game)
    if gameWin game then
        End
    else
        game.Entities |> List.map (fun p -> p.Player.Id) |> NightContext.New |> Night
}
