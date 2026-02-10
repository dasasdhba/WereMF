module WereMF.Role.PaoXian

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli

type PaoXianRole =
    | PaoXianRole
    interface IRole with
        member this.Base = {
            CharaType = PaoXian
            Priority = 0
            SummaryName = PaoXian.ToString ()
        }

let paoXianSendSkill ps game =
    let title = "输入一名玩家的编号令其死亡，输入 0 以放弃"
    let filter = filterGiveUp
                >> filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能杀死自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser
