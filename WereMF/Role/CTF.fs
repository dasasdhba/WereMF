module WereMF.Role.CTF

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Cli
open WereMF.State

type CTFRole =
    {
        BugCount : int
        Reborn: bool
    }
    static member New count = { BugCount = count; Reborn = false }
    interface IRole with
        member this.Base = {
            CharaType = CTF
            Priority = 3
            SummaryName = CTF.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Record [|
            "bug_count", decimal this.BugCount |> JsonValue.Number
            "reborn", JsonValue.Boolean this.Reborn
        |]
    interface IRolePreventDead with
        member this.Prevent dead = monad {
            if this.Reborn || dead = Force then None else
            
            let! entity, bind = State.get
            let main, game = bind
            
            let myBug = entity.State.BugCount
            if myBug >= 2 then None else
            
            let totalBug = game.Entities |> List.map (fun e -> e.State.BugCount) |> List.sum
            let totalBug = totalBug - myBug
            if totalBug <= 0 then None else
            
            let msg = { Type = ToPlayer entity.Player ; Content = "移动一只 bug 到自己身上并复活吗？（1：是；0：否）" }
            let yes = requestInputWithRawMessage msg "request_ctf_reborn" parseBool
            if yes |> not then None else
                
            let bugPlayer = game.Entities |> List.filter (
                    fun e -> e.Player.Id <> entity.Player.Id && e.State.BugCount > 0
                                ) |> List.randomChoiceWith main.Rng
            let bugPlayer = { bugPlayer with State.Bug =
                                             match bugPlayer.State.Bug with
                                             | Some 1 -> None
                                             | Some v -> Some (v - 1)
                                             | _ -> None }
            let game = game.UpdateEntity bugPlayer
            let bind = main, game
            do! State.put (entity, bind)
            
            Some { NewRole = { this with Reborn = true }; StateSetter = fun e -> { e with Bug = Some (myBug + 1) } }
        }