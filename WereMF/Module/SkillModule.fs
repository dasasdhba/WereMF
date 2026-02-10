module WereMF.Module.Skill

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.State

// ------------------------------------------------------------------
// helpers

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

let private getThreatenResult filter (game: GameContext) ps=
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
            Some (Ok { Pending = ps ; Target = target })
        else
            sendMessage { Type = ToPlayer entity.Player
                          Content = $"你被威胁{msg}" }
            Some (Error threaten)
    )
    
let private updateThreatenIfViolate targets result entity =
    match result with
    | Some (Error (threaten: ThreatenSkill)) ->
        if targets |> List.contains threaten.Target then entity
        else { entity with Entity.State.Threaten = Some { Type = NightSkill ; Source = threaten.Source } }
    | _ -> entity
    
let sendSkillWith title filter
    (parser: string -> Result<ISkill option list, string>) ps = monad {
    let! (game: GameContext, night: NightContext) = State.get
    let entity = game.GetEntity ps.Source
    let threaten = ps |> getThreatenResult filter game
    
    if game.Entities |> List.exists (fun e ->
        Ok e.Player.Id |> filter |> Result.isOk) |> not then
        let name = entity |> Entity.getHandlerName ps.Handler
        sendMessage { Type = ToPlayer entity.Player
                      Content = $"你的{name}技能不可用" }
        ()
    else
    
    match threaten with
    | Some (Ok v) ->
        if v.Target <= PlayerId 0 then () else
            let night = { night with Skills = v :: night.Skills }
            do! State.put (game, night)
        ()
    | _ ->
        let msg = { Type = ToPlayer entity.Player; Content = title }
        let results = requestInputWithMessage msg parser
        let targets = results |> List.map (function
            | Some skill -> skill.Target
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

type ISkillExecuteImmediate =
    abstract member CanExecute : unit ->
        Reader<MainContext * GameContext * NightContext, bool>
    abstract member Execute : unit ->
        State<MainContext * GameContext * NightContext, ISkillExecuteImmediate>

type ISkillExecuteSummary =
    abstract member CanExecute : unit ->
        Reader<MainContext * GameContext * NightContext, bool>
    abstract member Execute : unit ->
        State<MainContext * GameContext * NightContext, ISkillExecuteImmediate>