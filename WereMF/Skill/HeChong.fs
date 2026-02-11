module WereMF.Skill.HeChong

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli

type HeChongSkill =
    | HeChongSkill
    interface ISkill

let heChongSendSkill ps game =
    let title = "输入一名其他玩家的编号复制其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能复制自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = HeChongSkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r HeChongSkill |> Some ])
    ps |> sendSkillWith title filter parser def
