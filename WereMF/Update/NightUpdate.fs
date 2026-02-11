module WereMF.Update.Night

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Utils
open WereMF.Skill.Bind
open WereMF.State
open WereMF.Module

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

let rec private pendingSkills (context: SkillContext) =
    if context.Night.PendingSkills.Length = 0 then context else
    let next, remain = getNextPendingSkills context.Night.PendingSkills
    let next = next |> List.randomShuffleWith context.Main.Rng
    let g, n = sendPendingSkills context.Game context.Night next
    let n = { n with PendingSkills = remain }
    let context = { context with Game = g ; Night = n }
    let context = executeSkills context
    pendingSkills context

let nightAction night = monad {
    let! (main: MainContext, game : GameContext) = State.get
    let psList = createPendingSkills (game.Entities |> List.filter (
        fun e -> e |> Entity.getState |> EntityState.isDead |> not))
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
         fun s -> match s.Actor with
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

let nightSummary (night: NightContext) = monad {
    sendMessage { Type = Public; Content = "今晚" }
    for msg in night.Messages do
        sendMessage { Type = Public ; Content = msg }
    
    let! (main: MainContext, game : GameContext) = State.get
    
    let updateContext (c : RoleContext) (e :Entity) =
        { c with Game = c.Game.UpdateEntity e }
    
    // 虫子
    
    let mutable skills = night.QueuedSkills
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
            let c, _ = bug |> requestDead request roleContext
            roleContext <- c
     
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
            let c, _ = x |> requestDead request roleContext
            roleContext <- c
    
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
            let role, _ = r.Target |> requestDead r.Request role
            let main, game = role.Get ()
            let context = { context with Main = main ; Game = game }
            updateSkills context sList
        | _ -> updateSkills context sList
        
    let context = SkillContext.Create main game night
    let context = updateSkills context list
    
    do! State.put (context.Main, context.Game)
    ()
}

let nightUpdate (night : NightContext) = monad {
    let! (main :MainContext, game : GameContext) = State.get
    let _, (main, game) = State.run (nightStart ()) (main, game)
    let night, (main, game) = State.run (nightAction night) (main, game)
    let _, (main, game) = State.run (nightSummary night) (main, game)
    do! State.put (main, game)
    // TODO: maybe game win
    Day
}