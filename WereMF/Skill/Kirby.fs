module WereMF.Skill.Kirby

open FSharpPlus
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type KirbySkill =
    | KirbySkill
    interface ISkill

let kirbySendSkill ps game =
    let title = "输入一名玩家的编号吸入，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能吸入自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = KirbySkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r KirbySkill |> Some ])
    ps |> sendSkillWith title filter parser def
