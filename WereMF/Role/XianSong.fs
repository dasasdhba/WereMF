module WereMF.Role.XianSong

open System
open FSharpPlus
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type XianSongRole =
    {
        MfaList : PlayerId list
        Reborn : bool option
        Disabled : bool option
    }
    static member New () = { MfaList = [] ; Reborn = None ; Disabled = None }
    member this.IsDisabled () =
        this.Disabled.IsSome
    member this.IsReborn () =
        this.Reborn.IsSome
    member this.IsRebornChoice () =
        this.Reborn.IsSome && this.Reborn.Value = true
    interface IRole with
        member this.Base = {
            CharaType = XianSong
            Priority = 1
            SummaryName = XianSong.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = None }

type XianSongSkill =
    {
        Pending : PendingSkill
        Target : PlayerId
        ForceMfa : bool option  // None = 普通，Some true = 强制要mfa，Some false = 强制丢球
    }
    interface ISkill with
        member this.Pending = this.Pending
        member this.Target = this.Target

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
            | "m" -> Some true   // 强制要mfa
            | "x" -> Some false  // 强制丢球
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
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if disabled then filterDisabled "你被丟虫了，技能失效一晚" else id)
    let filter = giveUpOrFilterWith filter
    
    let parser (input: string) : Result<ISkill option list, string> = monad {
        let! targetId, forceMfa = parseXianSongInput input
        let! targetId = Ok targetId |> filter
        if targetId <= PlayerId 0 then [ None ] else

        if isRebornChoice |> not && forceMfa.IsSome then
            return! Error "你现在不能强制玩家的选择"
        elif forceMfa.IsSome && (mfaList |> List.contains targetId) then
            return! Error "该玩家已给出 mfa，不能强制其选择"
        else
            let skill = {
                Pending = ps
                Target = targetId
                ForceMfa = forceMfa
            }
            [ skill :> ISkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser
