module WereMF.Update.Night

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Game
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
        let e = c.Game.GetEntity id
        let n, e = updater c.Night e
        { c with Game = c.Game.UpdateEntity e ; Night = n }
    let source = skill.Pending.Source
    let target = skill.Target
    let entity = context.Game.GetEntity source
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
    let sEntity = context.Game.GetEntity source
    let blocked = (context.Night.GetPlayerState source).Blocked
    if blocked || sEntity |> Entity.getState |> EntityState.isDead then
        sendMessage { Type = ToPlayer sEntity.Player ; Content = "失败" }
        context, skill, false
    else
    
    if skill.Sending.Target <= PlayerId 0 then
        sendMessage { Type = Internal ; Content = $"无效的技能：{skill.Sending.Pending.Type}，请检查输入处理是否正确" }
        context, skill, false
    else
    
    // spring
    let skill = { skill with Sending = skill.Sending |> setSpring context }
    
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
        if context.Game.HasEntity target |> not then context, false else
        
        let state = context.Night.GetPlayerState target
        if state.Kirby.IsNone then context, false else
        
        let kirby, handler = state.Kirby.Value
        let state = { state with Kirby = None }
        let context = { context with Night = context.Night.SetPlayerState state }
        
        let kEntity = context.Game.GetEntity kirby
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
                let k = { k with CopiedRole = Some (createRole context.Main.Roll chara) }
                let kEntity = kEntity |> handler.SetToEntity k
                let context = { context with Game = context.Game.UpdateEntity kEntity }
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
    sendMessage { Type = Public ; Content = "晚上开始\n" + (printNightSummary game.Entities) }
    
    let rec updateNightDead idx (entities: Entity list) c =
        if idx >= entities.Length then c, entities else
        let (e: Entity) = entities[idx]
        let c, e = e |> Entity.updateOnNightStartRequestDead c
        let entities = entities |> List.updateAt idx e
        updateNightDead (idx + 1) entities c
    
    let context = RoleContext.Create main game
    let entities = game.Entities
    let context, entities = updateNightDead 0 entities context
    let main, game = context.Get ()
    let entities = entities |> List.map (Entity.updateOnNightStart main)
    let game = { game with Entities = entities }
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
    if context.Night.Skills.Length = 0 then context else
    let skill = context.Night.Skills.Head
    let context, skill, success = executeSkill context skill
    let context = { context with Night.Skills = context.Night.Skills.Tail }
    let context =
        if success |> not then context else
        { context with Night.QueuedSkills = context.Night.QueuedSkills @ [skill] }
    executeSkills context
    
let rec private executeQueuedSkills (context: SkillContext) =
    if context.Night.QueuedSkills.Length = 0 then context else
    let skill = context.Night.QueuedSkills.Head
    let context = { context with Night.QueuedSkills = context.Night.QueuedSkills.Tail }
    let skill, context =
        match skill.Actor with
        | :? ISkillExecuteQueued as exe ->
            let actor, context =
                State.run (exe.Execute skill.Sending) context
            { skill with Actor = actor }, context
        | _ -> skill, context
    let context = { context with Night.SummarySkills = context.Night.SummarySkills @ [skill] }
    executeQueuedSkills context

let rec private pendingSkills (context: SkillContext) =
    if context.Night.PendingSkills.Length = 0 then context else
    let next, remain = getNextPendingSkills context.Night.PendingSkills
    let next = next |> List.randomShuffleWith context.Main.Rng
    let g, n = sendPendingSkills context.Game context.Night next
    let n = { n with PendingSkills = remain }
    let context = { context with Game = g ; Night = n }
    let context = executeSkills context
    let context = executeQueuedSkills context
    pendingSkills context

let nightAction night = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let psList = createPendingSkills (game.Entities |> List.filter (
        fun e -> e |> Entity.getState |> EntityState.isDead |> not)) main.Rng
    let night = { night with PendingSkills = psList }
    let context = SkillContext.Create main game night
    let context = pendingSkills context
    let main, game, night = context.Get ()
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

let private involveIfDogeSummary (target: Entity) (night: NightContext) (role: RoleContext) (list: Skill list) =
    if target.State |> EntityState.isDead |> not then role, list else
    let prots = night.PlayerStates |> List.filter (fun ps ->
        ps.Id |> role.Game.GetEntity |> getState |> EntityState.isDead |> not
        && ps.Doge |> List.contains target.Player.Id)
    let mutable r, l = role, list
    for ps in prots do
        let entity = role.Game.GetEntity ps.Id
        let name = entity.Player.Name
        let msg = $"{target.Player.Name}保护了{name}"
        sendMessage { Type = Public ; Content = msg }
        let request = DeadRequest.New Kill
        let sk, heal = tryHeal l request entity
        if heal then
            l <- sk
        else
            let c, _ = entity |> requestDead request r
            r <- c
    r, l

let nightSummary (night: NightContext) = monad {
    sendMessage { Type = Public; Content = "今晚" }
    for msg in night.Messages do
        sendMessage { Type = Public ; Content = msg }
    
    let! (main: MainContext, game : GameContext) = State.get
    
    let updateContext (c : RoleContext) (e :Entity) =
        { c with Game = c.Game.UpdateEntity e }
    
    // 虫子
    
    let mutable skills = night.SummarySkills
    let mutable roleContext = RoleContext.Create main game
    let bugs = roleContext.Game.Entities |> List.filter (fun e -> e.State.BugCount >= 3)
    for bug in bugs do
        sendMessage { Type = Public ; Content = $"{bug.Player.Name}的虫子数量过多！" }
        let bug = { bug with State.Bug = None }
        roleContext <- bug |> updateContext roleContext
        let request = DeadRequest.New Sudden
        let sk, heal = tryHeal skills request bug
        if heal then
            skills <- sk
        else
            let c, bug = bug |> requestDead request roleContext
            roleContext <- c
            let r, l = involveIfDogeSummary bug night roleContext skills
            roleContext <- r
            skills <- l
     
    // 闲松球
    
    let xian = roleContext.Game.Entities
               |> List.filter (fun e -> e.State.XianSongCount >= 2
                                        || e.State.XianSong
                                        |> List.exists (fun x -> x <= 0))
    for x in xian do
        sendMessage { Type = Public ; Content = $"{x.Player.Name}身上的咸松球爆炸了！" }
        let x = { x with State.XianSong = [] }
        roleContext <- x |> updateContext roleContext
        let request = DeadRequest.New Sudden
        let sk, heal = tryHeal skills request x
        if heal then
            skills <- sk
        else
            let c, x = x |> requestDead request roleContext
            roleContext <- c
            let r, l = involveIfDogeSummary x night roleContext skills
            roleContext <- r
            skills <- l
    
    // 其他技能
    
    let main, game = roleContext.Get ()
    
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
            let t = summary.GetRealTarget s.Sending
            if t |> context.Game.HasEntity
               && t |> context.Game.GetEntity |> getState |> EntityState.isDead then
                updateSkills context sList
            else
                
            let r, context = State.run (summary.Summarize s.Sending) context
            if r.IsNone then updateSkills context sList else
            
            // try heal
            let r = r.Value
            let sList, success = tryHeal sList r.Request r.Target
            if success then updateSkills context sList else
            
            // dead request
            let role = RoleContext.Create context.Main context.Game
            let role, target = r.Target |> requestDead r.Request role
            let role, sList = involveIfDogeSummary target night role sList
            let main, game = role.Get ()
            let context = { context with Main = main ; Game = game }
            updateSkills context sList
        | _ -> updateSkills context sList
        
    let context = SkillContext.Create main game night
    let context = updateSkills context list
    
    do! State.put (context.Main, context.Game)
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
        sendMessage { Type = Public ; Content = $"/n{printSummary game.Entities}" }
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