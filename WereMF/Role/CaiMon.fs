module WereMF.Role.CaiMon

open System
open FSharpPlus
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type CaiMonRole =
    {
        CaiCount : int
        RebornRound : int option
        RebornList : PlayerId list
    }
    static member New () = { CaiCount = 3 ; RebornRound = None ; RebornList = [] }
    interface IRole with
        member this.Base = {
            CharaType = CaiMon
            Priority = 100
            SummaryName = CaiMon.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            match this.RebornRound with
            | None -> this
            | Some v -> { this with RebornRound = Some (v - 1) }
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            if this.RebornRound.IsSome && this.RebornRound.Value <= 0 then
                [DeadRequest.New Force]
            else
                []
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            if dead = Force || this.RebornRound.IsSome || this.CaiCount <= 1 then None else
            let msg = { Type = ToPlayer entity.Player ; Content = "用一根彩条复活吗？（1：是；0：否）" }
            let yes = requestInputWithMessage msg parseBool
            if yes |> not then None else
            Some {
                NewContext = context
                NewEntity = entity
                NewRole = { this with RebornRound = Some 2 ; CaiCount = this.CaiCount - 1 }
            }

type CaiMonSkill =
    {
        Pending : PendingSkill
        Target : PlayerId
        Double : bool  // true = 两根，false = 一根
    }
    interface ISkill with
        member this.Pending = this.Pending
        member this.Target = this.Target

// 解析彩条数量，格式: "玩家ID" 或 "玩家ID d"
let parseCaiMonInput (input: string) : Result<PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 ->
        parsePlayerId parts[0] |> Result.map (fun id -> (id, false))  // 默认一根
    | 2 ->
        let isDouble = parts[1].ToLower() = "d"
        if not isDouble then Error "请输入 d 表示用两根彩条"
        else parsePlayerId parts[0] |> Result.map (fun id -> (id, true))
    | _ -> Error "请输入格式: 玩家编号 [d]"

// 彩怪技能发送
let caiMonSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let caiCount =
        match ps.Handler.GetFromEntity entity with
        | :? CaiMonRole as caiMon -> caiMon.CaiCount
        | _ -> 0
    
    let title = $"输入要复活的死亡玩家编号，在结尾输入 d 表示使用两根彩条（剩余 {caiCount} 根彩条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterAlive game
                >> filterExceptIndex ps.Source "不能给自己彩条"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    
    let parser (input: string) : Result<ISkill option list, string> = monad {
        let! target, double = parseCaiMonInput input
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        
        if (double && caiCount < 2) || (double |> not && caiCount < 1) then
            return! Error "彩条不足"
        else
            let skill = {
                Pending = ps
                Target = target
                Double = double
            }
            [ skill :> ISkill |> Some ]
       }
    
    ps |> sendSkillWith title filter parser
