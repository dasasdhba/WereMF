module WereMF.Module.Skill

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.State

// ------------------------------------------------------------------
// sending helpers

let giveUpOrFilterWith filter = function
    | Ok p when p <= PlayerId 0 -> Ok (PlayerId 0)
    | value -> value |> filter
    
let filterNonExists (game: GameContext) = function
    | Ok p when game.HasEntity p |> not -> Error "玩家不存在"
    | value -> value

let filterExceptIndex idx hint = function
    | Ok p when p = idx -> Error hint
    | value -> value

let filterExceptIndexList idx hint = function
    | Ok p when idx |> List.contains p -> Error hint
    | value -> value

let filterDead (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.isDead
        -> Error "玩家已死亡"
    | value -> value
    
let filterAlive (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.isDead |> not
        -> Error "玩家未死亡"
    | value -> value

let filterSelectable (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.canBeSelected |> not
        -> Error "玩家无法选中"
    | value -> value
    
let filterSelectableWithoutSmog (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.canBeSelectedWithSmog |> not
        -> Error "玩家无法选中"
    | value -> value
    
let filterKidnapped ps = function
    | Ok p when ps.Kidnapped && p <> ps.Source -> Error "你被绑架"
    | value -> value
    
let filterDisabled hint = function
    | _ -> Error hint

let private getThreatenResult filter (creator: unit -> ISkill)
    (game: GameContext) ps=
    let entity = game.GetEntity ps.Source
    ps.Threaten |> Option.bind (fun threaten ->
        let target = threaten.Target
        if Ok target |> filter |> Result.isError then
            sendMessage { Type = ToPlayer (threaten.Source |> game.GetEntity).Player
                          Content = "威胁失败" }
            None
        else
            
        let msg = if target <= PlayerId 0 then "不发技能"
                  else $"技能发给 {(target |> game.GetEntity).Player.ToCliString ()}"
        
        if threaten.Force then
            sendMessage { Type = ToPlayer entity.Player
                          Content = $"你被强制威胁{msg}" }
            Some (Ok (Skill.New ps target (creator())))
        else
            sendMessage { Type = ToPlayer entity.Player
                          Content = $"你被威胁{msg}" }
            Some (Error threaten)
    )
    
let private updateThreatenIfViolate targets result entity =
    match result with
    | Some (Error (threaten: ThreatenSkill)) ->
        if targets |> List.contains threaten.Target then entity
        else { entity with Entity.State.Threaten = Some { Type = QueuedDeath ; Source = threaten.Source } }
    | _ -> entity
    
let sendSkillWith title filter
    (parser: string -> Result<Skill option list, string>) creator ps = monad {
    let! (game: GameContext, night: NightContext) = State.get
    let entity = game.GetEntity ps.Source
    let threaten = ps |> getThreatenResult filter creator game
    
    if game.Entities |> List.exists (fun e ->
        Ok e.Player.Id |> filter |> Result.isOk) |> not then
        let name = entity |> Entity.getHandlerName ps.Handler
        sendMessage { Type = ToPlayer entity.Player
                      Content = $"你的{name}技能不可用" }
        ()
    else
    
    match threaten with
    | Some (Ok v) ->
        if v.Sending.Target <= PlayerId 0 then () else
            let night = { night with Skills = v :: night.Skills }
            do! State.put (game, night)
        ()
    | _ ->
        let msg = { Type = ToPlayer entity.Player; Content = title }
        let results = requestInputWithMessage msg parser
        let targets = results |> List.map (function
            | Some skill -> skill.Sending.Target
            | None -> PlayerId 0)
        let entity = entity |> updateThreatenIfViolate targets threaten
        let game = game.UpdateEntity entity
        let skills = results |> List.choose id
        let night = { night with Skills = skills @ night.Skills }
        do! State.put (game, night)
        ()
}

// ------------------------------------------------------------------
// execution

type SkillContext =
    {
        Main : MainContext
        Game : GameContext
        Night : NightContext
    }
    static member Create main game night =
        {
            Main = main
            Game = game
            Night = night
        }
    member this.Get () =
        this.Main, this.Game, this.Night
        
type ISkillCanExecute =
    abstract member CanExecute : SkillContext -> SendingSkill -> bool

type ISkillExecute =
    abstract member Execute : SendingSkill ->
        State<SkillContext, ISkill>

type SkillDeadRequest = {
    Target : Entity
    Request : DeadRequest
}

type ISkillSummary =
    abstract member Priority : int
    abstract member GetRealTarget : SendingSkill -> PlayerId
    abstract member Summarize : SendingSkill ->
        State<SkillContext, SkillDeadRequest option>
        
type ISkillHealDeadKill =
    abstract member CanHeal : unit -> bool
    abstract member Heal : string -> ISkill
    
type ISkillHealDeadSudden =
    abstract member CanHeal : unit -> bool
    abstract member Heal : string -> ISkill

// ------------------------------------------------------------------
// executing helpers

let getRealTarget (skill: SendingSkill) =
    match skill.Spring with
    | None -> skill.Target
    | Some Normal -> skill.Pending.Source
    | Some Recursed -> PlayerId 0

let updateBugWith (context: SkillContext) (skill : SendingSkill) =
    let update (updater : NightContext -> Entity -> NightContext * Entity) (c: SkillContext) (id: PlayerId) =
        let e = c.Game.GetEntity id
        let n, e = updater c.Night e
        { c with Game = c.Game.UpdateEntity e ; Night = n }
    match skill.Spring with
    | None ->
        let context = update updateBugOnNight context skill.Pending.Source
        update updateBugOnNight context skill.Target
    | Some Normal ->
        let context = update updateBugOnNight context skill.Pending.Source
        let context = update updateBugOnNight context skill.Target
        update updateBugOnNight context skill.Pending.Source
    | Some Recursed ->
        let context = update updateSpringBugOnNight context skill.Pending.Source
        update updateSpringBugOnNight context skill.Target

let canExecute context (skill: Skill) =
    match skill.Actor with
    | :? ISkillCanExecute as actor -> skill.Sending |> actor.CanExecute context
    | _ ->
        // by default, we check recursed and if target alive
        let target = skill.Sending |> getRealTarget
        if context.Game.HasEntity target |> not then false else
        let entity = context.Game.GetEntity target
        entity.State |> EntityState.isDead |> not

let setSpring context (skill: SendingSkill) =
    if (context.Night.GetPlayerState skill.Target).Spring then
        if (context.Night.GetPlayerState skill.Pending.Source).Spring then
            { skill with Spring = Some Recursed }
        else
            { skill with Spring = Some Normal }
    else
        { skill with Spring = None }
        
let isDoged (night: NightContext) target =
    let state = night.GetPlayerState target
    state.Doge.IsSome
    
let getSource (skill : SendingSkill) =
    skill.Pending.Source
    
let getHandler (skill: SendingSkill) =
    skill.Pending.Handler
    
let getSenderName (game: GameContext) (skill :SendingSkill) =
    let source = skill |> getSource
    let handler = skill.Pending.Handler
    let entity = game.GetEntity source
    getHandlerName handler entity
    
let getPlayerName (game: GameContext) (player : PlayerId)=
    let entity = game.GetEntity player
    entity.Player.Name