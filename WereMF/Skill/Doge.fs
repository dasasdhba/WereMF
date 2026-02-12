module WereMF.Skill.Doge

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.Doge

// DogeSkill 类型，包含自爆信息
type DogeSkill =
    {
        IsSuicide : bool
    }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName context.Game
            let recv = target |> getPlayerName context.Game
            if target |> isDoged context.Night then
                let night = context.Night.AddMessage $"{sender}想保护{recv}，被 doge 挡了"
                do! State.put { context with Night = night }
                this
            else
                let state = context.Night.GetPlayerState target
                let state = { state with Doge = Some sending.Pending.Source }
                let night = context.Night.SetPlayerState state
                do! State.put { context with Night = night }
                this
        }
    interface ISkillSummary with
        member this.Priority = -1
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.IsSuicide |> not then None else
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            sendMessage { Type = Public ; Content = $"{entity.Player.Name}自爆了！" }
            Some {
                Target = entity
                Request = DeadRequest.New Force
            }
        }

// 获取上一晚保护过的玩家列表
let getLastNightProtected (handler: RoleHandler) (entity: Entity) : PlayerId list =
    match handler.GetFromEntity entity with
    | :? DogeRole as dogeRole ->
        dogeRole.LastSelected.Selected
    | _ -> []

// 解析 Doge 的输入，格式: "玩家ID [b]"
// 返回 (目标玩家ID, 是否自爆)
let parseDogeInput (input: string) : Result<PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 0 -> Error "输入不能为空"
    | 1 ->
        // 仅保护，不自爆
        parsePlayerId parts[0] |> Result.map (fun id -> (id, false))
    | 2 ->
        // 保护 + 自爆
        let isSuicide = parts[1].ToLower() = "b"
        if not isSuicide then
            Error "无效输入格式，请使用: 玩家ID [b]"
        else
            parsePlayerId parts[0] |> Result.map (fun id -> (id, true))
    | _ ->
        Error "无效输入格式，请使用: 玩家ID [b]"

// Doge 技能发送
let dogeSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let lastNightList = getLastNightProtected ps.Handler entity
    
    let title = "输入要保护的玩家编号（输入 0 放弃），结尾加 b 表示自爆；输入 0 放弃"
    
    let filter = filterNonExists game
                    >> filterDead game
                    >> filterSelectable game
                    >> filterKidnapped ps
                    >> filterExceptIndexList lastNightList "不能连续保护同一个玩家"
    let filter = giveUpOrFilterWith filter
    let def () =
        let msg = { Type = ToPlayer entity.Player ; Content = "你可以选择是否自爆（1：是；0：否）" }
        let yes = requestInputWithMessage msg parseBool
        { IsSuicide = yes } :> ISkill
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! target, isSuicide = parseDogeInput input
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        let dogeSkill = Skill.New ps target { IsSuicide = isSuicide }
        [ dogeSkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
