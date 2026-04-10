module WereMF.Role.JiangXian

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Role

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
        member this.ToJsonValue () = JsonValue.Record [|
            "dead_voted", JsonValue.Boolean this.DeadVoted 
        |]
    interface IRoleUpdateOnVoteEnd with
        member this.Update entity game = monad {
            if entity |> Entity.isDayBlocked then this else
            let! day = State.get
            
            if entity.State |> EntityState.isDead |> not then
                let msg = { Type = ToPlayer entity.Player; Content = "输入你真正想投的票" }
                let parser = parsePlayerId >> (voteTargetFilter entity.Player.Id game)
                let result = requestInputWithMessage msg parser
                let state = day.GetPlayerVote entity.Player.Id
                let state = { state with Target = Some result }
                let day = day.SetPlayerVote state
                do! State.put day
                this
            elif this.DeadVoted then this else
            
            let msg = { Type = ToPlayer entity.Player; Content = "你有一次死亡后投票的机会，输入你想投票的玩家，输入 0 放弃" }
            let parser = parsePlayerId >> (voteTargetFilter entity.Player.Id game)
            let result = requestInputWithMessage msg parser
            if result <= PlayerId 0 then this else
            let state = day.GetPlayerVote entity.Player.Id
            let state = { state with Target = Some result }
            let day = day.SetPlayerVote state
            do! State.put day
            { this with DeadVoted = true }
        }