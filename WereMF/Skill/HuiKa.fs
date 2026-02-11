module WereMF.Skill.HuiKa

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.HuiKa

type HuiKaSkill =
    | HuiKaSkill
    interface ISkill

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
    let def () = HuiKaSkill :> ISkill
    
    let createSkill id = Skill.New ps id HuiKaSkill
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser def
