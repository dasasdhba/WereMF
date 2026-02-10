module WereMF.Role.CTF

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type CTFRole =
    {
        BugCount : int
        Reborn : bool
    }
    static member New count = { BugCount = count ; Reborn = false }
    interface IRole with
        member this.Base = {
            CharaType = CTF
            Priority = 3
            SummaryName = CTF.ToString ()
        }

// CTF技能发送
let ctfSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let bugCount =
        match ps.Handler.GetFromEntity entity with
        | :? CTFRole as ctf -> ctf.BugCount
        | _ -> 0
    
    let title = $"输入要释放虫子的玩家编号（剩余 {bugCount} 只虫子），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "不能给自己虫子"
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if bugCount <= 0 then filterDisabled "你没有虫子了" else id)
    let filter = giveUpOrFilterWith filter
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    
    ps |> sendSkillWith title filter parser
