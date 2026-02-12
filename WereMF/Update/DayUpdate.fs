module WereMF.Update.Day

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Game
open WereMF.Module.Role
open WereMF.Role.JiangXian
open WereMF.Role.JiaoHua
open WereMF.State
open WereMF.Update.Night

let private voteSourceFilter (game: GameContext) = function
    | Ok id when game.HasEntity id |> not ->
        Error "该玩家不存在"
    | Ok id ->
        let e = game.GetEntity id
        if e.State |> EntityState.isDead then Error "该玩家已死亡"
        elif e.State |> EntityState.canVote |> not then Error "该玩家不能投票"
        else Ok id
    | value -> value
    
let private voteTargetFilter (game: GameContext) = function
    | Ok id when id <= PlayerId 0 -> Ok (PlayerId 0)
    | Ok id when game.HasEntity id |> not ->
        Error "目标不存在"
    | Ok id ->
        let e = game.GetEntity id
        if e.State |> EntityState.isDead then Error "目标已死亡"
        elif e.State |> EntityState.canBeVoted |> not then Error "目标不可选中"
        else Ok id
    | value -> value

let private voteJiaoHuaFilter (game: GameContext) = function
    | Ok id when id <= PlayerId 0 -> Ok (PlayerId 0)
    | Ok id when game.HasEntity id |> not ->
        Error "目标不存在"
    | Ok id ->
        let e = game.GetEntity id
        if e.State |> EntityState.isDead then Error "目标已死亡"
        elif e.State.LeafProtected.IsSome then Error "目标不可选中"
        elif e.State.JiaoHuaVoteBlocked then Error "目标已被禁票"
        else Ok id
    | value -> value

let private updateJiaoHuaBlocked (gameContext: GameContext)=
    let rec update (i: int) (game: GameContext) =
        let rec updateHandlers (j: int) (g: GameContext) (e: Entity) (hs : RoleHandler list) =
            if j >= hs.Length then g else
            let h = hs[j]
            let role = h.GetFromEntity e
            match role with
            | :? JiaoHuaRole as jiaoHua when jiaoHua.VoteBlock ->
                if g.Entities |> List.exists (fun p -> Ok p.Player.Id |> voteJiaoHuaFilter g |> Result.isOk ) then
                    let parser input =
                        input |> parsePlayerId |> voteJiaoHuaFilter g
                    let msg = { Type = Public ; Content = $"{e.Player.Name}可以禁票一人（输入要禁票的玩家编号，输入 0 放弃）" }
                    let r = requestInputWithMessage msg parser
                    if r <= PlayerId 0 then updateHandlers (j + 1) g e hs else
                    let e = g.GetEntity r
                    let e = { e with State.JiaoHuaVoteBlocked = true }
                    let g = g.UpdateEntity e
                    updateHandlers (j + 1) g e hs
                else
                    updateHandlers (j + 1) g e hs
            | _ -> updateHandlers (j + 1) g e hs
        if i >= game.Entities.Length then game else
        let e = game.Entities[i]
        let handlers = e.Role |> getValidHandlers
        let game = updateHandlers 0 game e handlers
        update (i + 1) game
    update 0 gameContext

let private updateThreaten (game: GameContext) =
    let rec update (i: int) (g: GameContext) =
        if i >= g.Entities.Length then g else
        let e = g.Entities[i]
        let t = e.State.Threaten
        if t.IsNone then update (i + 1) g else
        let src = t.Value.Source
        let se = g.GetEntity src
        if se.State |> EntityState.isDead then
            let e = { e with State.Threaten = None }
            let g = g.UpdateEntity e
            update (i + 1) g
        else
            update (i + 1) g
    update 0 game

let private updateForceThreaten (game: GameContext) (day: DayContext)=
    let rec update (i: int) (g: GameContext) (votes: (Entity * PlayerId) list) (d: DayContext) =
        if i >= g.Entities.Length then g, d else
        let e = g.Entities[i]
        let t = votes |> tryFind (fun (e, _) -> e.Player.Id = e.Player.Id)
        if t.IsNone then update (i + 1) g votes d else
        let _, target = t.Value
        
        if Ok target |> voteTargetFilter g |> Result.isOk then
            let msg = if target <= PlayerId 0 then $"{e.Player.Name}被强制弃票"
                      else $"{e.Player.Name}被强制把票投给{target}"
            sendMessage { Type = Public ; Content = msg }
            let vote = d.GetPlayerVote e.Player.Id
            let vote = { vote with Target = Some target ; Confirmed = true }
            let d = d.SetPlayerVote vote
            update (i + 1) g votes d
        else
            let src = e.State.Threaten.Value.Source
            let se = g.GetEntity src
            let msg = { Type = ToPlayer se.Player ; Content = "失败" }
            sendMessage msg
            let e = { e with State.Threaten = None }
            let g = g.UpdateEntity e
            update (i + 1) g votes d
        
    let threaten = game.Entities |> List.map (fun e ->
        match e.State.Threaten with
        | Some t ->
            match t.Type with
            | DayVote (target, force) when force -> Some (e, target)
            | _ -> None
        | None -> None
        )
    let threaten = threaten |> List.choose id
    update 0 game threaten day

let dayStart (day: DayContext) = monad {
    let! (main: MainContext, game : GameContext) = State.get
    sendMessage { Type = Public ; Content = "白天开始\n" + (printDaySummary game.Entities) }
    
    let rec updateDayDead idx (entities: Entity list) c =
        if idx >= entities.Length then c, entities else
        let (e: Entity) = entities[idx]
        let c, e = e |> Entity.updateOnDayStartRequestDead c
        let entities = entities |> List.updateAt idx e
        updateDayDead (idx + 1) entities c
    
    let context = RoleContext.Create main game
    let entities = game.Entities
    let context, entities = updateDayDead 0 entities context
    let main, game = context.Get ()
    let entities = entities |> List.map Entity.updateOnDayStart
    let game = { game with Entities = entities }
                   |> updateJiaoHuaBlocked
                   |> updateThreaten
    
    let game, day = updateForceThreaten game day
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
        let! x = parts[0] |> parsePlayerId |> voteSourceFilter game
        let xEntity = game.GetEntity x
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
            
        let! y = y |> parsePlayerId |> voteTargetFilter game
        VoteNormal (x, y)
    | _ ->
        return! Error "未知格式"
}

let updateVote (x: PlayerId) (y: PlayerId) (day: DayContext) =
    let vote = day.GetPlayerVote x
    let vote =
        match vote.Target with
        | Some _ -> { vote with Target = Some y ; Confirmed = true }
        | None -> { vote with Target = Some y }
    day.SetPlayerVote vote

let canAnyoneVote (game: GameContext) (day: DayContext) =
    day.Votes |> List.exists (fun v -> Ok v.Id |> voteTargetFilter game |> Result.isOk) &&
    day.Votes |> List.exists (fun v ->
        Ok v.Id |> voteSourceFilter game |> Result.isOk && v.Confirmed |> not)

let private getVoteString (vote: PlayerVote) =
    match vote.Target with
    | None -> ""
    | Some v when v = PlayerId 0 && vote.Confirmed -> "：弃票√"
    | Some v when v = PlayerId 0 -> "：弃票"
    | Some v when vote.Confirmed -> $"：{v}√"
    | Some v -> $"：{v}"

let printVoteSummary (game: GameContext) (day: DayContext) =
    game.Entities |> printSummaryWith (fun e ->
        if e.State |> EntityState.isDead then e |> getDaySummary else
        let vote = day.GetPlayerVote e.Player.Id
        (e |> getDaySummary) + getVoteString vote
    )
    
type JiaoHuaAction =
    | Blocked
    | Protected

let private parseJiaoHuaInput (game: GameContext) (input: string) : Result<PlayerId * JiaoHuaAction, string> = monad {
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 when parts[0] = "0" -> PlayerId 0, Blocked
    | 2 ->
        let! playerId = parsePlayerId parts[0]
        let! playerId = Ok playerId |> (voteTargetFilter game)
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
    
    sendMessage { Type = Public
                  Content = $"{entity.Player.Name}可以取消一人下一个晚上的一次行动，或令一人不可被其他人的技能选中" }
    let msg = { Type = ToPlayer entity.Player
                Content = "输入玩家编号和行动类型（x=封住行动，p=保护玩家），输入 0 放弃" }
    let target, action = requestInputWithMessage msg (parseJiaoHuaInput game)
    if target <= PlayerId 0 then game else
    
    let tEntity = game.GetEntity target
    match action with
    | Blocked ->
        let tEntity = { tEntity with State.JiaoHuaBlocked = tEntity.State.JiaoHuaBlocked + 1 }
        game.UpdateEntity tEntity
    | Protected ->
        let tEntity = { tEntity with State.JiaoHuaProtected = true }
        game.UpdateEntity tEntity
        
let private updateIfShiWuOut (entity : Entity) (role: RoleContext) =
    if entity.State |> EntityState.isDead |> not then role else
    
    let rec update (kids: PlayerId list) (r: RoleContext) =
        if kids.Length = 0 then r else
        let k = r.Game.GetEntity kids.Head
        sendMessage { Type = Public
                      Content = $"由于{entity.Player.Name}绑架了{k.Player.Name}" }
        let request = DeadRequest.New Kill
        let r, k = requestDead request r k
        let g = updateIfJiaoHuaOut k r.Game
        let r = { r with Game = g }
        update kids.Tail r
    let kidnapped = role.Game.Entities |> List.filter (fun e ->
        e.State.Kidnapped |> List.contains entity.Player.Id) |> List.map (fun e -> e.Player.Id)
    update kidnapped role

let private updateIfVoteOut (entity : Entity) (role: RoleContext) =
    let g = updateIfJiaoHuaOut entity role.Game
    let role = { role with Game = g }
    updateIfShiWuOut entity role
    
let requestVoteOut (entity : Entity) (role: RoleContext) =
    let request = DeadRequest.New Vote
    let role, entity = requestDead request role entity
    updateIfVoteOut entity role
    
let VoteOutIfTooManyGiveUp (day: DayContext) (role: RoleContext) =
    let alive = role.Game.Entities |> List.filter (fun e ->
        e.State |> EntityState.isDead |> not)
    let half = (float alive.Length) / 2.0 |> Math.Ceiling |> int
    let giveUp = day.Votes |> List.filter (fun v ->
        alive |> List.exists (fun q -> q.Player.Id = v.Id )
        && v.GetTarget () = PlayerId 0)
    if giveUp.Length < half then role else
    
     sendMessage { Type = Public; Content = "超过半数的玩家弃票了！度娘要抽贴了！" }
     let r = giveUp |> List.randomChoiceWith role.Main.Rng
     let rEntity = role.Game.GetEntity r.Id
     sendMessage { Type = Public; Content = $"{rEntity.Player.Name}被抽贴了！" }
     requestVoteOut rEntity role

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
        if e.State.BarLeader.IsSome && e.State.BarLeader.Value then 2 else 1
    getVoteOutPlayerWith adder day
    
let getVoteOutPlayer (day : DayContext) (game: GameContext) =
    let p1 = getVoteOutPlayerNormal day
    let p2 = getVoteOutPlayerWithBarLeader day game
    if p1 = p2 then p1, game else
    let bar = game.Entities |> List.find (fun e -> e.State.BarLeader.IsSome)
    let bar = { bar with State.BarLeader = Some false }
    let game = game.UpdateEntity bar
    p2, game

let private setVoteIfJiangXian (entity : Entity) (day: DayContext) (game: GameContext) =
    if Ok entity.Player.Id |> voteSourceFilter game |> Result.isError then day, entity else
    
    let handlers = entity.Role |> getValidHandlers |> List.filter (fun h ->
        getHandlerCharaType h entity = JiangXian)
    if handlers.Length = 0 then day, entity else
    
    if entity.State |> EntityState.isDead |> not then
        let msg = { Type = ToPlayer entity.Player; Content = "输入你真正想投的票" }
        let parser = parsePlayerId >> (voteTargetFilter game)
        let result = requestInputWithMessage msg parser
        let state = day.GetPlayerVote entity.Player.Id
        let state = { state with Target = Some result }
        let day = day.SetPlayerVote state
        day, entity
    else
    
    let rs = handlers |> List.map (fun h ->
        let r = h.GetFromEntity entity
        match r with
        | :? JiangXianRole as j when j.DeadVoted |> not -> Some (h, j)
        | _ -> None
    )
    let rs = rs |> List.choose id
    if rs.Length = 0 then day, entity else
    let h, j = rs.Head
    let msg = { Type = ToPlayer entity.Player; Content = "输入你想投票的玩家，输入 0 放弃" }
    let parser = parsePlayerId >> (voteTargetFilter game)
    let result = requestInputWithMessage msg parser
    if result <= PlayerId 0 then day, entity else
    let state = day.GetPlayerVote entity.Player.Id
    let state = { state with Target = Some result }
    let day = day.SetPlayerVote state
    let j = { j with DeadVoted = true }
    let entity = h.SetToEntity j entity
    day, entity
        
let setJiangXianVote (day: DayContext) (game: GameContext) =
    let mutable d, g = day, game
    for e in game.Entities do
        let nd, ne = setVoteIfJiangXian e d g
        d <- nd
        g <- g.UpdateEntity ne
    d, g
    
let private updateThreatenVote (day: DayContext) (game: GameContext) =
    let rec update (i: int) (g: GameContext) =
        if i >= g.Entities.Length then g else
        let e = g.Entities[i]
        let t = e.State.Threaten
        if t.IsNone then update (i + 1) g else
        match t.Value.Type with
        | QueuedDeath -> update (i + 1) g
        | DayVote (target, _) ->
            let real = (day.GetPlayerVote e.Player.Id).GetTarget()
            let e =
                if real = target then e
                else { e with State.Threaten = Some { t.Value with Type = QueuedDeath } }
            let g = g.UpdateEntity e
            update (i + 1) g
    update 0 game

let private updateBombVote (day: DayContext) (game: GameContext) =
    let rec update (i: int) (g: GameContext) =
        if i >= g.Entities.Length then g else
        let e = g.Entities[i]
        let t = (day.GetPlayerVote e.Player.Id).GetTarget()
        if t <= PlayerId 0 then update (i + 1) g else
        let b = e.State.Bomb
        let e = { e with State.Bomb = e.State.Bomb - b }
        let te = g.GetEntity t
        let te = { te with State.Bomb = te.State.Bomb + b }
        let g = g.UpdateEntity e
        let g = g.UpdateEntity te
        update (i + 1) g
    update 0 game
    
let dayVote (day : DayContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    
    let rec voteRec d =
        let msg = { Type = Internal ; Content = "输入 x y 表示 x 给 y 投票，若 x 是脚滑人，可以输入 x b 自爆；输入 0 结束投票环节" }
        let vote = requestInputWithMessage msg (parseVote game d)
        match vote with
        | VoteNormal (x, y) ->
            let d = updateVote x y d
            sendMessage { Type = Public ; Content = $"\n{printVoteSummary game d}" }
            if canAnyoneVote game d then
                voteRec d
            else
                Finish, d
        | v -> v, d
    
    let result, day = voteRec day
    sendMessage { Type = Public ; Content = "投票结束" }
    match result with
    | Suicide x ->
        let xEntity = game.GetEntity x
        sendMessage { Type = Public ; Content = $"{xEntity.Player.Name}自爆了" }
        let role = RoleContext.Create main game
        let role = requestVoteOut xEntity role
        let main, game = role.Get ()
        do! State.put (main, game)
    | _ ->
        sendMessage { Type = Public ; Content = $"\n{printVoteSummary game day}" }
        let role = RoleContext.Create main game
        let role = VoteOutIfTooManyGiveUp day role
        let main, game = role.Get ()
        let day, game = setJiangXianVote day game
        let game = updateThreatenVote day game
        let game = updateBombVote day game
        let out, game = getVoteOutPlayer day game
        if out <= PlayerId 0 then
            sendMessage { Type = Public ; Content = "平票" }
            do! State.put (main, game)
        else
            let o = game.GetEntity out
            let role = RoleContext.Create main game
            let role = requestVoteOut o role
            let main, game = role.Get ()
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
