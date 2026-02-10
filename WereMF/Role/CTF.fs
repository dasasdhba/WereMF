module WereMF.Role.CTF

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type CTFRole =
    {
        BugCount : int
    }
    static member New count = { BugCount = count }
    interface IRole with
        member this.Base = {
            CharaType = CTF
            Priority = 3
            SummaryName = CTF.ToString ()
        }
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            if dead = Force then None else
            
            let myBug = entity.State.Bug
            if myBug >= 2 then None else
             
            let totalBug = context.Game.Entities |> List.map (fun e -> e.State.Bug) |> List.sum
            let totalBug = totalBug - myBug
            if totalBug <= 0 then None else
            
            let msg = { Type = ToPlayer entity.Player ; Content = "移动一只 bug 到自己身上并复活吗？（1：是；0：否）" }
            let yes = requestInputWithMessage msg parseBool
            if yes |> not then None else
                
            let bugPlayer = context.Game.Entities |> List.filter (
                    fun e -> e.Player.Id <> entity.Player.Id && e.State.Bug > 0
                                ) |> List.randomChoiceWith context.Main.Rng
            let bugPlayer = { bugPlayer with State.Bug = bugPlayer.State.Bug - 1 }
            let entity = { entity with State.Bug = entity.State.Bug + 1 }
            let context = { context with Game = context.Game.UpdateEntity bugPlayer }
            
            Some {
                NewContext = context
                NewEntity = entity
                NewRole = this
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
