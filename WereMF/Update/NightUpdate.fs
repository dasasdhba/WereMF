module WereMF.Update.Night

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Role.Bind
open WereMF.Role.Kirby
open WereMF.Skill.Bind
open WereMF.Skill.CTF
open WereMF.State
open WereMF.Module

let private updateBugWith (context: SkillContext) (skill : SendingSkill) =
    let update (updater : NightContext -> Entity -> NightContext * Entity) (c: SkillContext) (id: PlayerId) =
        let (m, g), n = c
        let e = g.GetEntity id
        let n, e = updater n e
        let g = g.UpdateEntity e
        ((m, g), n) |> SkillContext
    let source = skill.Pending.Source
    let target = skill.Target
    let (main, game), night = context
    let entity = game.GetEntity source
    match skill.Spring with
    | None ->
        let context = update updateBugOnNight context source
        update updateBugOnNight context skill.Target
    | Some Normal ->
        let context =
            // CTF 被弹簧弹了虫子，此时只判定一次
            if skill.Pending.Type = CTF && entity.State.BugCount = 0 then context else
            update updateBugOnNight context source
        let context = update updateBugOnNight context target
        update updateBugOnNight context source
    | Some Recursed ->
        let context = update updateSpringBugOnNight context source
        update updateSpringBugOnNight context target

let private executeSkill (context: SkillContext) (skill : Skill) =
    let source = skill.Sending.Pending.Source
    let (main, game), night = context
    let sEntity = game.GetEntity source
    let blocked = (night.GetPlayerState source).Blocked
    if blocked || sEntity |> Entity.getState |> EntityState.isDead then
        sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
        context, skill, false
    else
    
    if skill.Sending.Target <= PlayerId 0 then
        sendMessage { Type = Internal ; Content = $"无效的技能：{skill.Sending.Pending.Type}，请检查输入处理是否正确" }
        context, skill, false
    else
    
    // spring
    let skill = { skill with Sending = skill.Sending |> setSpring night }
    
    // cost
    let context, skill =
        match skill.Actor with
        | :? ISkillCost as cost ->
            let actor, context = (State.run (cost.Cost skill.Sending) context)
            let skill = { skill with Actor = actor }
            context, skill
        | _ -> context, skill
    
    // kirby
    let target = skill.Sending |> getRealTarget
    let context, success =
        let (main, game), night = context
        if game.HasEntity target |> not then context, false else
        
        let state = night.GetPlayerState target
        if state.Kirby.IsNone then context, false else
        
        let kirby, handler = state.Kirby.Value
        let state = { state with Kirby = None }
        let night = night.SetPlayerState state
        let context = (main, game), night
        
        let kEntity = game.GetEntity kirby
        let chara = skill.Sending.Pending.Type
        if chara = Kirby then
            sendMessage { Type = ToPlayer kEntity.Player ; Content = "失败" }
            sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
            context, true
        else
            sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
            sendMessage { Type = ToPlayer kEntity.Player ; Content = chara.ToString () }
            let role = handler.GetFromEntity kEntity
            match role with
            | :? KirbyRole as k ->
                let k = { k with CopiedRole = Some (createRole main.Roll chara) }
                let kEntity = kEntity |> handler.SetToEntity k
                let game = game.UpdateEntity kEntity
                let context = (main, game), night
                context, true
            | _ ->
                context, true
    
    // general
    let skill, context, success =
        if success then skill, context, false else
        match skill.Actor with
        | :? ISkillExecute as exe ->
            let (actor, context), success =
                if skill |> canExecute context then
                    (State.run (exe.Execute skill.Sending) context), true
                else
                    sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
                    (skill.Actor, context), false
            let skill = { skill with Actor = actor }
            skill, context, success
        | _ -> skill, context, false
        
    let context = skill.Sending |> updateBugWith context
    context, skill, success

let private createPendingSkills (entities: Entity list) (rng : Random) =
    let rec jiaoHuaBlock remaining (list: PendingSkill list) (player: Player) =
        if remaining = 0 || list.Length = 0 then list else
        let list = list |> List.randomShuffleWith rng
        let blocked = list.Head
        sendMessage { Type = ToPlayer player; Content = $"你的{blocked.Type.ToString()}被禁用" }
        jiaoHuaBlock (remaining - 1) list.Tail player
    let mutable result = []
    for e in entities do
        let h = getPendingHandlers e.Player e.Role
        let s = h |> List.map (fun u -> e |> Entity.createPendingSkill u)
        let s = jiaoHuaBlock e.State.JiaoHuaBlocked s e.Player
        result <- result @ s
    result

let nightStart () = monad {
    let! (main: MainContext, game : GameContext) = State.get
    sendMessage { Type = Public ; Content = "晚上开始" }
    
    let main, game =
        [0..(game.Entities.Length - 1)] |> List.fold (fun (m, g) i ->
            let e = g.Entities[i]
            let e, (m, g) = updateOnNightStartRequestDead (e, (m, g))
            m, g
        ) (main, game)
    
    let entities = game.Entities |> List.map (Entity.updateOnNightStart main)
    let game = { game with Entities = entities }
    sendMessage { Type = Public ; Content = "\n" + (printNightSummary game.Entities) }
    do! State.put (main, game)
}

let private getNextPendingSkills (psList : PendingSkill list) =
    match psList with
    | [] -> [], []
    | _ ->
        let maxPriority = psList |> List.maxBy (fun ps -> ps.Priority) |> _.Priority
        let selected = psList |> List.filter (fun ps -> ps.Priority = maxPriority)
        let remaining = psList |> List.filter (fun ps -> ps.Priority <> maxPriority)
        selected, remaining
        
let rec private sendPendingSkills (game: GameContext) (night: NightContext) (psList: PendingSkill list) =
    if psList.Length = 0 then game, night else
    let ps = psList.Head
    let _, (game, night) = State.run (sendSkill game ps) (game, night)
    sendPendingSkills game night psList.Tail

let rec private executeSkills (context: SkillContext) =
    let (main, game), night = context
    if night.Skills.Length = 0 then context else
    let skill = night.Skills.Head
    let context, skill, success = executeSkill context skill
    let (main, game), night = context
    let night = { night with Skills = night.Skills.Tail }
    let night =
        if success |> not then night else
        { night with QueuedSkills = night.QueuedSkills @ [skill] }
    executeSkills ((main, game), night)
    
let rec private executeQueuedSkills (context: SkillContext) =
    let (main, game), night = context
    if night.QueuedSkills.Length = 0 then context else
    let skill = night.QueuedSkills.Head
    let night = { night with QueuedSkills = night.QueuedSkills.Tail }
    let context = (main, game), night
    let skill, context =
        match skill.Actor with
        | :? ISkillExecuteQueued as exe ->
            let actor, context =
                State.run (exe.Execute skill.Sending) context
            { skill with Actor = actor }, context
        | _ -> skill, context
    let (main, game), night = context
    let night = { night with SummarySkills = night.SummarySkills @ [skill] }
    executeQueuedSkills ((main, game), night)

let rec private pendingSkills (context: SkillContext) =
    let (main, game), night = context
    if night.PendingSkills.Length = 0 then context else
    let next, remain = getNextPendingSkills night.PendingSkills
    let next = next |> List.randomShuffleWith main.Rng
    let game, night = sendPendingSkills game night next
    let night = { night with PendingSkills = remain }
    let context = (main, game), night
    let context = executeSkills context
    let context = executeQueuedSkills context
    pendingSkills context

let nightAction night = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let psList = createPendingSkills (game.Entities |> List.filter (
        fun e -> e |> Entity.getState |> EntityState.isDead |> not)) main.Rng
    let night = { night with PendingSkills = psList }
    let context = (main, game), night
    let context = pendingSkills context
    let (main, game), night = context
    do! State.put (main, game)
    night
}

let private tryHeal (list: Skill list) (request: DeadRequest) (target: Entity) =
    let t = request.DeadType
    if t = Force || t = Vote then list, false else
    
    let idx = list |> List.tryFindIndex (
         fun s -> if s.Sending |> getRealTarget <> target.Player.Id then false else
                  match s.Actor with
                  | :? ISkillHealDeadKill as k when t = Kill && k.CanHeal () -> true
                  | :? ISkillHealDeadSudden as s when t = Sudden && s.CanHeal () -> true
                  | _ -> false
         )
    match idx with
    | None -> list, false
    | Some idx ->
        let name = request.GetName target
        let h = list[idx]
        let a =
            match h.Actor with
            | :? ISkillHealDeadKill as k when t = Kill -> k.Heal name
            | :? ISkillHealDeadSudden as s when t = Sudden -> s.Heal name
            | _ -> h.Actor
        let h = { h with Actor = a }
        let list = list |> List.updateAt idx h
        list, true

let rec private involveIfDogeSummary (target: Entity) (context: SkillContext) (list: Skill list) =
    let (main, game), night = context
    if target.State |> EntityState.isDead |> not then context, list else
    let prots = night.PlayerStates |> List.filter (fun ps ->
        ps.Id |> game.GetEntity |> getState |> EntityState.isDead |> not
        && ps.Doge |> List.contains target.Player.Id)
    let mutable c, l = context, list
    for ps in prots do
        let (main, game), night = c
        let ps = { ps with Doge = ps.Doge |> List.filter (fun id -> id <> target.Player.Id) }
        let night = night.SetPlayerState ps
        let entity = game.GetEntity ps.Id
        let name = entity.Player.Name
        let msg = $"{target.Player.Name}保护了{name}"
        sendMessage { Type = Public ; Content = msg }
        let request = DeadRequest.New Kill
        let sk, heal = tryHeal l request entity
        if heal then
            l <- sk
        else
            let dead = entity, (main, game)
            let entity, (main, game) = requestDead request dead
            let rc, rl = involveIfDogeSummary entity ((main, game), night) l
            c <- rc
            l <- rl
    c, l

let nightSummary (night: NightContext) = monad {
    sendMessage { Type = Public; Content = "今晚" }
    for msg in night.Messages do
        sendMessage { Type = Public ; Content = msg }
    
    let! (main: MainContext, game : GameContext) = State.get
    
    // 虫子
    
    let mutable skills = night.SummarySkills
    let mutable context = (main, game), night
    let bugs = game.Entities |> List.filter (fun e -> e.State.BugCount >= 3)
    for bug in bugs do
        let (main, game), night = context
        sendMessage { Type = Public ; Content = $"{bug.Player.Name}的虫子数量过多！" }
        let bug = { bug with State.Bug = None }
        let game = game.UpdateEntity bug
        context <- (main, game), night
        let request = DeadRequest.New Sudden
        let sk, heal = tryHeal skills request bug
        if heal then
            skills <- sk
        else
            let dead = bug, (main, game)
            let bug, (main, game) = requestDead request dead
            let r, l = involveIfDogeSummary bug ((main, game), night) skills
            context <- r
            skills <- l
     
    // 闲松球
    
    let (main, game), night = context
    let xian = game.Entities
               |> List.filter (fun e -> e.State.XianSongCount >= 2
                                        || e.State.XianSong
                                        |> List.exists (fun x -> x <= 0))
    for x in xian do
        let (main, game), night = context
        sendMessage { Type = Public ; Content = $"{x.Player.Name}身上的咸松球爆炸了！" }
        let x = { x with State.XianSong = [] }
        let game = game.UpdateEntity x
        context <- (main, game), night
        let request = DeadRequest.New Sudden
        let sk, heal = tryHeal skills request x
        if heal then
            skills <- sk
        else
            let dead = x, (main, game)
            let x, (main, game) = requestDead request dead
            let r, l = involveIfDogeSummary x ((main, game), night) skills
            context <- r
            skills <- l
    
    // 其他技能
    
    let (main, game), night = context
    
    let list = skills |> List.sortByDescending (fun s ->
            match s.Actor with
            | :? ISkillSummary as summary -> summary.Priority
            | _ -> -100
        )
    
    let rec updateSkills (context: SkillContext) (sList : Skill list) =
        if sList.Length = 0 then context else
        let s = sList.Head
        let sList = sList.Tail
        match s.Actor with
        | :? ISkillSummary as summary ->
            let (m, g), n = context
            let t = summary.GetRealTarget s.Sending
            if t |> g.HasEntity
               && t |> g.GetEntity |> getState |> EntityState.isDead then
                updateSkills context sList
            else
                
            let r, context = State.run (summary.Summarize s.Sending) context
            if r.IsNone then updateSkills context sList else
            
            // try heal
            let r = r.Value
            let sList, success = tryHeal sList r.Request r.Target
            if success then updateSkills context sList else
            
            // dead request
            let (m, g), n = context
            let dead = r.Target, (m, g)
            let target, (m, g) = requestDead r.Request dead
            let context, sList = involveIfDogeSummary target ((m, g), n) sList
            updateSkills context sList
        | _ -> updateSkills context sList
        
    let context = (main, game), night
    let context = updateSkills context list
    
    let (main, game), _ = context
    do! State.put (main, game)
    ()
}

let gameWin (game: GameContext) : bool =
    let alive = game.Entities |> List.filter (fun e -> e.State |> EntityState.isDead |> not)
    let result =
        if alive.Length = 0 then
            sendMessage { Type = Public ; Content = "游戏结束，无人生还" }
            true
        elif alive |> List.forall (fun e -> e |> getCamp = Bar) then
            sendMessage { Type = Public ; Content = "游戏结束，吧方获胜" }
            true
        elif alive |> List.forall (fun e -> e |> getCamp = Boom) then
            sendMessage { Type = Public ; Content = "游戏结束，爆方获胜" }
            true
        elif alive |> List.forall (fun e -> e |> getCamp = Yezi) then
            sendMessage { Type = Public ; Content = "游戏结束，叶子获胜" }
            true
        else
            false
    
    if result then
        sendMessage { Type = Public ; Content = $"\n{printSummary game.Entities}" }
    result
    
let nightUpdate (night : NightContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let _, (main, game) = State.run (nightStart ()) (main, game)
    let night, (main, game) = State.run (nightAction night) (main, game)
    let _, (main, game) = State.run (nightSummary night) (main, game)
    do! State.put (main, game)
    if gameWin game then
        End
    else
        DayContext.New (game.Entities |> List.map (fun e -> e.Player.Id)) |> Day
}