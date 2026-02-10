module WereMF.Role.JiaoHua

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli

type JiaoHuaRole =
    | JiaoHuaRole
    interface IRole with
        member this.Base = {
            CharaType = JiaoHua
            Priority = 5
            SummaryName = JiaoHua.ToString ()
        }

let jiaoHuaSendSkill ps game =
    let title = "输入一名玩家的编号查询其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能查自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser