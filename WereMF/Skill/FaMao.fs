module WereMF.Skill.FaMao

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.FaMao

type FaMaoSkill =
    | FaMaoSkill
    interface ISkill

// 获取最大投掷数量（第一晚2瓶，之后1瓶）
let getFaMaoMaxCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? FaMaoRole as faMaoRole -> if faMaoRole.FirstRound then 1 else 2
    | _ -> 1

// 法猫技能发送
let faMaoSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let maxCount = getFaMaoMaxCount ps.Handler entity
    
    let title = $"输入要投掷药水的玩家编号（最多 {maxCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = maxCount
        MaxCountError = Some $"最多投掷 {maxCount} 瓶药水"
        DuplicateError = Some "不能重复投掷同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = FaMaoSkill :> ISkill
    
    let createSkill id = Skill.New ps id FaMaoSkill
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser def
