module WereMF.Skill.JiangXian

open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Skill
open WereMF.Module.Api

type JiangXianSkill =
    | JiangXianSkill
    interface ISkill

let jiangXianSendSkill ps game =
    let title = "江仙设计未完成"
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> filterDisabled "设计未完成"
    let filter = giveUpOrFilterWith filter
    let def () = JiangXianSkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r JiangXianSkill |> Some ])
    ps |> sendSkillWith title ApiType.RequestJiangxianSkill filter parser def
