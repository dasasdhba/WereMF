module WereMF.Role.JiaoHua

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Cli
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Api
open WereMF.State

let private voteJiaoHuaFilter (game: GameContext) = function
    | Ok id when id <= PlayerId 0 -> Ok (PlayerId 0)
    | Ok id when game.HasEntity id |> not ->
        Error "目标不存在"
    | Ok id ->
        let e = game.GetEntity id
        if e.State |> EntityState.isDead then Error "目标已死亡"
        elif e.State.LeafProtected.IsSome then Error "目标不可选中"
        elif e.State.JiaoHuaVoteBlocked then Error "目标已被禁票"
        else Ok id
    | value -> value

type JiaoHuaRole =
    {
        VoteBlock : bool
    }
    static member New () = { VoteBlock = false }
    interface IRole with
        member this.Base = {
            CharaType = JiaoHua
            Priority = 5
            SummaryName = JiaoHua.ToString ()
        }
        member this.ToJsonValue () = JsonValue.Null
    interface IRoleUpdateOnNightInit with
        member this.Update () =
            { this with VoteBlock = false }
    interface IRoleUpdateOnDead with
        member this.Update dead =
            if dead <> Kill then this else { this with VoteBlock = true }
    interface IRoleUpdateOnVoteStart with
        member this.Update player = monad {
            let! game = State.get
            let entity = game.GetEntity player.Id
            
            if entity.State |> EntityState.isDead |> not || this.VoteBlock |> not then this else
            let game =
                let filter p = p |> voteJiaoHuaFilter game
                if game.Entities |> List.exists (fun p ->
                    Ok p.Player.Id |> filter |> Result.isOk) |> not then game else
                
                let parser input =
                    input |> parsePlayerId |> voteJiaoHuaFilter game
                sendRawMessage { Type = Public ; Content = $"{player.Name}可以禁票一人" } ApiType.JiaohuaVoteBlockBroadcast
                let msg = {
                    Type = ToPlayer player
                    Content = "输入要禁票的玩家编号，输入 0 放弃"
                    Api = ApiType.RequestJiaohuaVoteBlock
                    Data = game.Entities |> createInvalidChoiceArray filter
                }
                let r = requestInputWithMessage msg parser
                if r <= PlayerId 0 then game else
                let e = game.GetEntity r
                let e = { e with State.JiaoHuaVoteBlocked = true }
                sendRawMessage { Type = Public ; Content = $"{e.Player.Name}被{player.Name}禁票" }
                               ApiType.JiaohuaVoteBlockBroadcast
                game.UpdateEntity e
            do! State.put game
            { this with VoteBlock = false }
        }
