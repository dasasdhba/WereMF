module WereMF.Role.CTF

open WereMF.Common
open WereMF.Module.Role
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
            
            let myBug = entity.State.BugCount
            if myBug >= 2 then None else
             
            let totalBug = context.Game.Entities |> List.map (fun e -> e.State.BugCount) |> List.sum
            let totalBug = totalBug - myBug
            if totalBug <= 0 then None else
            
            let msg = { Type = ToPlayer entity.Player ; Content = "移动一只 bug 到自己身上并复活吗？（1：是；0：否）" }
            let yes = requestInputWithMessage msg parseBool
            if yes |> not then None else
                
            let bugPlayer = context.Game.Entities |> List.filter (
                    fun e -> e.Player.Id <> entity.Player.Id && e.State.BugCount > 0
                                ) |> List.randomChoiceWith context.Main.Rng
            let bugPlayer = { bugPlayer with State.Bug =
                                             match bugPlayer.State.Bug with
                                             | Some 1 -> None
                                             | Some v -> Some (v - 1)
                                             | _ -> None }
            let entity = { entity with State.Bug = Some (myBug + 1) }
            let context = { context with Game = context.Game.UpdateEntity bugPlayer }
            
            Some {
                NewContext = context
                NewEntity = entity
                NewRole = this
            }
