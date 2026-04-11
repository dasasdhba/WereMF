module WereMF.Update.Game

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Module
open WereMF.Update.Day
open WereMF.Update.Night

let gameInit (main : MainContext) (game: GameContext) =
    let rng = main.Rng
    
    // 吧主
    
    let bars = game.Entities |> List.filter (fun e -> e |> Entity.getCamp = Bar)
    let hua = bars |> List.tryFind (fun e -> e |> Entity.getCharaType = JiaoHua)
    let bars = if hua.IsSome then bars |> List.filter (fun e -> e <> hua.Value) else bars
    let barLeader = bars |> List.randomChoiceWith rng
    let barLeader = { barLeader with State.BarLeader = Some true }
    let game = game.UpdateEntity barLeader
    let message =
        match hua with
        | Some v ->
            $"你是吧主，脚滑人是 {v.Player.ToInGameString ()}"
        | None ->
            "你是吧主，本局没有脚滑人"
    sendRawMessage { Type = ToPlayer barLeader.Player ; Content = message } ApiType.BarleaderNotify
    
    // 脚滑人知道一组身份
    
    monad {
        let! hua = hua
        let bars = bars |> List.filter (fun e -> e |> Entity.getCharaType <> JiaoHua)
        let booms = game.Entities |> List.filter (fun e -> e |> Entity.getCamp = Boom)
        let bar = bars |> List.randomChoiceWith rng
        let boom = booms |> List.randomChoiceWith rng
        let msg = $"本局有{(bar |> Entity.getCharaType).ToString()}和{(boom |> Entity.getCharaType).ToString()}"
        sendRawMessage { Type = ToPlayer hua.Player ; Content = msg } ApiType.JiaohuaStartNotify
    } |> ignore
    
    // 贤松知道炮仙
    
    monad {
        let! xian = game.Entities |> List.tryFind (fun e -> e |> Entity.getCharaType = XianSong)
        let msg =
            match game.Entities |> List.tryFind (fun e -> e |> Entity.getCharaType = PaoXian) with
            | Some pao ->
                $"炮仙是 {pao.Player.ToInGameString()}"
            | None ->
                "本局没有炮仙"
       sendRawMessage { Type = ToPlayer xian.Player ; Content = msg } ApiType.XiansongStartNotify
    } |> ignore
    
    game
    
let gameUpdate (game: GameState) = monad {
    let! main = State.get
    let status, (main, gc) =
        match game.Status with
        | Start ->
           let context = gameInit main game.Context
           let game = { game with Context = context }
           main.Players |> List.map (fun p -> p.Id) |> NightContext.New |> Night, (main, game.Context)
        | Night night -> State.run (nightUpdate night) (main, game.Context)
        | Day day -> State.run (dayUpdate day) (main, game.Context)
        | End ->
            let msg = { Type = Internal ; Content = "开启下一局？（1：是；0：否）" }
            let result = requestInputWithRawMessage msg ApiType.RequestForNextGame parseBool
            if result then
                raise (Restart |> CommandEx)
            else
                raise (Reboot |> CommandEx)
    
    let game = { game with Context = gc ; Status = status }
    do! State.put main
    game |> Game
}