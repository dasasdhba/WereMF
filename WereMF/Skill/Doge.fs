module WereMF.Skill.Doge

open System
open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Role.Doge

// DogeSkill 类型，包含自爆信息
type DogeSkill =
    {
        IsSuicide : bool
        Success : PlayerId option
    }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (d: DogeRole) -> { d with LastSelected = d.LastSelected.Add target
                                                            SelfSelected = d.SelfSelected || target = source
                                                      })
                             handler
            let game = entity |> game.UpdateEntity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想保护{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                { this with Success = Some target }
        }
    interface ISkillExecuteQueued with
        member this.Execute sending = monad {
            if this.Success.IsNone then this else
            let! (main, game), night = State.get
            let source = sending |> getSource
            let target = this.Success.Value
            let state = night.GetPlayerState target
            let state = { state with Doge = source :: state.Doge }
            let night = night.SetPlayerState state
            do! State.put ((main, game), night)
            this
        }
    interface ISkillSummary with
        member this.Priority = -1
        member this.GetRealTarget sending =
            sending |> getSource
        member this.Summarize sending = monad {
            if this.IsSuicide |> not then None else
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            sendRawMessage { Type = Public ; Content = $"{entity.Player.Name}自爆了！" } ApiType.DogeSuicideBroadcast
            Some {
                Target = entity
                Request = DeadRequest.New Force
            }
        }

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
    let handler = ps.Handler
    let lastNightList, selfProtected =
        match handler.GetFromEntity entity with
        | :? DogeRole as dogeRole ->
            dogeRole.LastSelected.Selected, dogeRole.SelfSelected
        | _ -> [], false
    
    let title = "输入要保护的玩家编号（输入 0 放弃），结尾加 b 表示自爆；输入 0 放弃"
    
    let filter = filterNonExists game
                    >> filterDead game
                    >> filterSelectable ps.Source game
                    >> filterKidnapped ps
                    >> filterExceptIndexList lastNightList "不能连续保护同一个玩家"
                    >> (if selfProtected then filterExceptIndex ps.Source "你保护过自己了" else id)
                    
    let filter = giveUpOrFilterWith filter
    let def () =
        let msg = {
            Type = ToPlayer entity.Player
            Content = "你可以选择是否自爆（1：是；0：否）"
            Api = ApiType.RequestDogeSkillForceThreaten
            Data = JsonValue.Record [|
                "skill_id", ps.ToJsonValue ()
                "pending_role", (ps.Handler.GetFromEntity entity).ToJsonValue ()
            |]
        }
        let yes = requestInputWithMessage msg parseBool
        { IsSuicide = yes ; Success = None } :> ISkill
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! target, isSuicide = parseDogeInput input
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        let dogeSkill = Skill.New ps target { IsSuicide = isSuicide ; Success = None }
        [ dogeSkill |> Some ]
    }
    
    ps |> sendSkillWith title ApiType.RequestDogeSkill filter parser def
