module WereMF.Skill.XianSong

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.ShiWu
open WereMF.State
open WereMF.Role.XianSong

type XianSongSkill =
    {
        ForceMfa : bool option
        Ball : PlayerId option
        Explosion: bool
    }
    static member New forceMfa =
        { ForceMfa = forceMfa ; Ball = None; Explosion = false }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            if this.ForceMfa.IsNone then this else

            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (x: XianSongRole) -> { x with Reborn = Some false })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let mfas = entity |> getFromRoleWithHandler
                                (fun (x: XianSongRole) -> x.MfaList)
                                 handler
            let target = sending |> getRealTarget
            let forceBall = mfas |> List.contains target
                            || (this.ForceMfa.IsSome && this.ForceMfa.Value = false)
            let target = sending |> getRealTarget

            let tEntity = target |> game.GetEntity
            let entity =
                if tEntity.State.BugCount <= 0 then entity else
                entity |> updateRoleWithHandler
                         (fun (x: XianSongRole) -> { x with Disabled = Some true })
                         handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)

            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let msg = if forceBall then $"{sender}想给{recv}丢咸松球，被Doge挡了"
                          else $"{sender}想找{recv}要mfa，被Doge挡了"
                sendRawMessage { Type = ToPlayer entity.Player ; Content = "失败" } "xiansong_skill_fail_by_doge_notify"
                let night = night.AddMessage msg
                do! State.put ((main, game), night)
                this
            else

            let mfa =
                if forceBall then false
                elif this.ForceMfa.IsSome && this.ForceMfa.Value then true
                else
                    let msg = { Type = ToPlayer tEntity.Player; Content = "你被要mfa了，给吗？（1：给；0：不给）" }
                    requestInputWithRawMessage msg "request_xiansong_give_mfa" parseBool
            
            if mfa then
                let entity = entity |> updateRoleWithHandler
                                      (fun (x: XianSongRole) -> { x with MfaList = target :: x.MfaList
                                                                         CanReborn = true })
                                      handler
                let game = game.UpdateEntity entity

                let th = tEntity |> Entity.getQueriedHandler main.Rng

                if th.IsNone then
                    sendRawMessage { Type = ToPlayer entity.Player; Content = "你要到mfa了，但是对面的身份不明" } "xiansong_get_mfa_smog_notify"
                    do! State.put ((main, game), night)
                    this
                else

                let th = th.Value
                let tEntity = target |> game.GetEntity
                let tEntity = tEntity |> exposeIfShiWu th
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)

                let name = tEntity |> Entity.getHandlerName th
                sendRawMessage { Type = ToPlayer entity.Player; Content = $"你要到mfa了，对面的身份是{name}" } "xiansong_get_mfa_notify"
                this
            else
                if forceBall |> not then
                    sendRawMessage { Type = ToPlayer entity.Player; Content = "你没有要到mfa" } "xiansong_get_mfa_fail_notify"
                { this with Ball = Some target }
        }
    interface ISkillExecuteQueued with
        member this.Execute sending = monad {
            if this.Ball.IsNone then this else
            let target = this.Ball.Value
            let! (main, game), night = State.get
            let entity = game.GetEntity target
            let entity = { entity with State.XianSong = entity.State.XianSong + 1 }
            let game = game.UpdateEntity entity
            let recv = target |> getPlayerName game
            let night = night.AddMessage $"{recv}被丢了咸松球"
            if entity.State.XianSong < 2 then
                do! State.put ((main, game), night)
                this
            else
                let state = night.GetPlayerState target
                let state = { state with Blocked = true }
                let night = night.SetPlayerState state
                do! State.put ((main, game), night)
                { this with Explosion = true }
        }
    interface ISkillSummary with
        member this.Priority = 9
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Explosion |> not then None else
            let! (main, game), night = State.get
            
            let target = sending |> getRealTarget
            let recv = target |> getPlayerName game
            let entity = game.GetEntity target
            let entity = { entity with State.XianSong = 0 }
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            
            sendRawMessage { Type = Public; Content = $"{recv}身上的咸松球爆炸了！" } "xiansong_boom_broadcast"
            Some {
                Target = entity
                Request = DeadRequest.New Sudden
            }
        }

// 解析贤松输入，格式: "玩家ID" 或 "玩家ID m" 或 "玩家ID x"
// m = 强制要mfa，x = 强制丢球
let parseXianSongInput (input: string) : Result<PlayerId * bool option, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 ->
        parsePlayerId parts[0] |> Result.map (fun id -> (id, None))
    | 2 ->
        let forceMfa =
            match parts[1].ToLower() with
            | "m" -> Some true
            | "x" -> Some false
            | _ -> None
        if forceMfa.IsNone then
            Error "请输入 m（强制要mfa）或 x（强制丢咸松球）"
        else
            parsePlayerId parts[0] |> Result.map (fun id -> (id, forceMfa))
    | _ -> Error "请输入格式: 玩家编号 [m/x]"

// 贤松技能发送
let xianSongSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let disabled, isRebornChoice, mfaList =
        match ps.Handler.GetFromEntity entity with
        | :? XianSongRole as xianSong ->
            xianSong.IsDisabled(), xianSong.IsRebornChoice(), xianSong.MfaList
        | _ -> false, false, []
    
    let title = "输入一名其他玩家的编号索要 mfa 文件，输入 0 放弃"
    let title = if isRebornChoice then title + "；在结尾输入 m 或者 x 表示强制要 mfa 或丢咸松球"
                else title
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "不能向自己要 mfa"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> (if disabled then filterDisabled "你被丟虫了，技能失效一晚" else id)
    let filter = giveUpOrFilterWith filter
    let def () =
        if isRebornChoice |> not then XianSongSkill.New None :> ISkill else
        let msg = { Type = ToPlayer entity.Player ; Content = "你可以输入 m 或者 x 表示强制要 mfa 或丢咸松球，输入 0 放弃" }
        let parser (input: string) =
            match input.Trim().ToLower() with
            | "m" -> Some true |> Ok
            | "x" -> Some false |> Ok
            | "0" -> Ok None
            | _ -> Error "未知格式"
        let yes = requestInputWithRawMessage msg "request_xiansong_skill_force_threaten" parser
        XianSongSkill.New yes :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! targetId, forceMfa = parseXianSongInput input
        let! targetId = Ok targetId |> filter
        if targetId <= PlayerId 0 then [ None ] else

        if isRebornChoice |> not && forceMfa.IsSome then
            return! Error "你现在不能强制玩家的选择"
        elif forceMfa.IsSome && (mfaList |> List.contains targetId) then
            return! Error "该玩家已给出 mfa，不能强制其选择"
        else
            let skill = Skill.New ps targetId (XianSongSkill.New forceMfa)
            [ skill |> Some ]
    }
    
    ps |> sendSkillWith title "request_xiansong_skill" filter parser def
