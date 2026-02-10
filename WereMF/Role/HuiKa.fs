module WereMF.Role.HuiKa

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type HuiKaRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = HuiKa
            Priority = 8
            SummaryName = HuiKa.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with FirstRound = true }

// 获取最大投掷数量（第一轮2个，之后1个）
let getHuiKaMaxCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? HuiKaRole as huiKa -> if huiKa.FirstRound then 1 else 2
    | _ -> 1

// 灰卡比技能发送
let huiKaSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let maxCount = getHuiKaMaxCount ps.Handler entity
    
    let title = $"输入要投掷烟雾弹的玩家编号（最多 {maxCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = maxCount
        MaxCountError = Some $"最多投掷 {maxCount} 个烟雾弹"
        DuplicateError = Some "不能重复投掷同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectableWithoutSmog game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    
    let createSkill id = { Pending = ps; Target = id } :> ISkill
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser
