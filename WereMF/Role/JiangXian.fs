module WereMF.Role.JiangXian

open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Skill

type JiangXianRole =
    {
        DeadVoted : bool
    }
    static member New () = { DeadVoted = false }
    interface IRole with
        member this.Base = {
            CharaType = JiangXian
            Priority = 0
            SummaryName = JiangXian.ToString ()
        }

let jiangXianSendSkill ps game =
    let title = "江仙设计未完成"
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterDisabled "设计未完成"
    let filter = giveUpOrFilterWith filter
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser