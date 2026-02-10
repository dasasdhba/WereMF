module WereMF.Role.Mole

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli

type MoleRole =
    | MoleRole
    interface IRole with
        member this.Base = {
            CharaType = Mole
            Priority = 0
            SummaryName = Mole.ToString ()
        }

let moleSendSkill ps game =
    let title = "输入一名玩家的编号进行突击，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能突击自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser
