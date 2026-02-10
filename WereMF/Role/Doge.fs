module WereMF.Role.Doge

open System
open FSharpPlus
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
    
    let parser (input: string) : Result<ISkill option list, string> = monad {
        let! target, isSuicide = parseDogeInput input
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        let dogeSkill = {
            Pending = ps
            Target = target
            IsSuicide = isSuicide
        }
        [ dogeSkill :> ISkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser
