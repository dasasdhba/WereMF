module WereMF.Module.Skill

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.State

// ------------------------------------------------------------------
// helpers

let private filterGiveUp = function
    | Ok p when p <= PlayerId 0 -> Ok p
    | value -> value
    
let private filterNonExists (game: GameContext) = function
    | Ok p when game.HasEntity p |> not -> Error "玩家不存在"
    | value -> value

let private filterExceptIndex idx hint = function
    | Ok p when p = idx -> Error hint
    | value -> value

let private filterDead (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.isDead
        -> Error "玩家已死亡"
    | value -> value

let private filterSelectable (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.canBeSelected |> not
        -> Error "玩家无法选中"
    | value -> value
    
let private filterSelectableWithoutSmog (game: GameContext) = function
    | Ok p when p |> game.GetEntity |> Entity.getState |> EntityState.canBeSelectedWithSmog |> not
        -> Error "玩家无法选中"
    | value -> value

let private getThreatenResult filter (game: GameContext) ps=
    let entity = game.GetEntity ps.Source
    ps.Threaten |> Option.bind (fun threaten ->
        let target = threaten.Target
        let t = target |> game.GetEntity
        if Ok target |> filter |> Result.isError then
            sendMessage { Type = ToPlayer (threaten.Source |> game.GetEntity).Player
                          Content = "威胁失败" }
            None
        else
        if threaten.Force then
            sendMessage { Type = ToPlayer entity.Player
                          Content = $"你被强制威胁技能发给 {t.Player.ToCliString ()}" }
            Some (Ok { Pending = ps ; Target = target })
        else
            sendMessage { Type = ToPlayer entity.Player
                          Content = $"你被威胁技能发给 {t.Player.ToCliString ()}" }
            Some (Error threaten)
    )
    
let private updateThreatenIfViolate target result entity =
    match result with
    | Some (Error (threaten: ThreatenSkill)) ->
        if threaten.Target = target then entity
        else { entity with Entity.State.Threaten = Some { Type = NightSkill ; Source = threaten.Source } }
    | _ -> entity
    
let private updateSendingSkill (game: GameContext) night threaten target ps =
    let entity = game.GetEntity ps.Source
    let entity = entity |> updateThreatenIfViolate target threaten
    let game = game.UpdateEntity entity
    if target <= PlayerId 0 then game, night else
    let skill = { Pending = ps ; Target = target }
    let night = { night with Skills = skill :: night.Skills }
    game, night
    
let private updateThreatenIfViolateGroup targets result entity =
    match result with
    | Some (Error (threaten: ThreatenSkill)) ->
        if targets |> List.contains threaten.Target then entity
        else { entity with Entity.State.Threaten = Some { Type = NightSkill ; Source = threaten.Source } }
    | _ -> entity
    
let private updateSendingSkills (game: GameContext) night threaten targets ps =
    let entity = game.GetEntity ps.Source
    let entity = entity |> updateThreatenIfViolateGroup targets threaten
    let game = game.UpdateEntity entity
    let skills = targets
                 |> List.map (fun t ->
                        if t <= PlayerId 0 then None
                        else Some { Pending = ps ; Target = t } )
                 |> List.choose id
    let night = { night with Skills = skills @ night.Skills }
    game, night

// ------------------------------------------------------------------
// sending

let jiaoHuaSendSkill ps = monad {
    let! (game: GameContext, night: NightContext) = State.get
    let entity = game.GetEntity ps.Source
    let name = entity |> Entity.getHandlerName ps.Handler
    if ps.Kidnapped then 
        sendMessage { Type = ToPlayer entity.Player
                      Content = $"你被绑架（{name}）" }
        Ok ()
    else
    
    let filter = filterGiveUp
                >> filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能查自己"
                >> filterSelectable game
    
    let threaten = ps |> getThreatenResult filter game
    
    if game.Entities |> List.exists (fun e ->
        Ok e.Player.Id |> filter |> Result.isOk) |> not then
        sendMessage { Type = ToPlayer entity.Player
                      Content = $"你的{name}技能不可用" }
        Ok ()
    else
    
    match threaten with
    | Some (Ok v) ->
        let night = { night with Skills = v :: night.Skills }
        do! State.put (game, night)
        Ok ()
    | _ ->
        let msg = { Type = ToPlayer entity.Player
                    Content = "输入一名玩家的编号查询其身份，输入 0 以放弃" }
        let parser = parsePlayerId >> filter
        let request = requestInputWithMessage msg parser
        match request with
        | Ok target ->
            let game, night = ps |> updateSendingSkill game night threaten target
            do! State.put (game, night)
            Ok ()
        | Error e -> Error e
}

// ------------------------------------------------------------------
// execution

type ISkillExecuteImmediate =
    abstract member Execute : Skill ->
        Reader<MainContext * GameContext * NightContext,
            State<MainContext * GameContext * NightContext,
                Result<ISkillExecuteImmediate, CommandType>>>

type ISkillExecuteSummary =
    abstract member Execute : Skill ->
        Reader<MainContext * GameContext * NightContext,
            State<MainContext * GameContext * NightContext,
                Result<ISkillExecuteSummary, CommandType>>>
                
// ------------------------------------------------------------------
// bind

let sendSkill (ps: PendingSkill) =
    match ps.Type with
    | JiaoHua -> jiaoHuaSendSkill ps
    | _ -> failwith "not implemented"