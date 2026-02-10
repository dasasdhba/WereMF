module WereMF.Role.Doge

open System
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type DogeRole =
    {
        LastSelected : SelectionState
    }
    static member New () = { LastSelected = SelectionState.New () }
    interface IRole with
        member this.Base = {
            CharaType = Doge
            Priority = 10
            SummaryName = Doge.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }

// DogeSkill 类型，包含自爆信息
type DogeSkill =
    {
        Pending : PendingSkill
        Target : PlayerId
        IsSuicide : bool
    }
    interface ISkill with
        member this.Pending = this.Pending
        member this.Target = this.Target

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
        parsePlayerId parts.[0] |> Result.map (fun id -> (id, false))
    | 2 ->
        // 保护 + 自爆
        let isSuicide = parts.[1].ToLower() = "b"
        if not isSuicide then
            Error "无效输入格式，请使用: 玩家ID [b]"
        else
            parsePlayerId parts.[0] |> Result.map (fun id -> (id, true))
    | _ ->
        Error "无效输入格式，请使用: 玩家ID [b]"

// 创建过滤函数：排除上一晚保护过的人
let filterLastNightProtected (lastNightList: PlayerId list) = function
    | Ok p when lastNightList |> List.contains p -> Error "不能连续保护同一个玩家"
    | value -> value

// Doge 技能发送
let dogeSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let lastNightList = getLastNightProtected ps.Handler entity
    
    let title = "输入要保护的玩家编号（输入 0 放弃），结尾加 b 表示自爆，如: 3 b"
    
    let baseFilter = filterGiveUp
                    >> filterNonExists game
                    >> filterDead game
                    >> filterSelectable game
                    >> filterKidnapped ps
                    >> filterLastNightProtected lastNightList
    
    let parser (input: string) : Result<ISkill option list, string> =
        match parseDogeInput input with
        | Ok (targetId, isSuicide) when targetId <= PlayerId 0 ->
            // 放弃
            Ok [ None ]
        | Ok (targetId, isSuicide) ->
            // 保护目标（可能自爆）
            match Ok targetId |> baseFilter with
            | Error e -> Error e
            | Ok _ ->
                let dogeSkill = {
                    Pending = ps
                    Target = targetId
                    IsSuicide = isSuicide
                }
                Ok [ dogeSkill :> ISkill |> Some ]
        | Error e -> Error e
    
    ps |> sendSkillWith title baseFilter parser
