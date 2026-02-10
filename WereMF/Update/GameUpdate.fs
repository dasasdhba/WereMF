module WereMF.Update.Game

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.State
open WereMF.Module
open WereMF.Update.Night

let gameInit (main : MainContext) (game: GameContext) =
    let rng = main.Rng
    
    // 吧主
    
    let bars = game.Entities |> List.filter (fun e -> e |> Entity.getCamp = Bar)
    let hua = bars |> List.tryFind (fun e -> e |> Entity.getCharaType = JiaoHua)
    let bars = if hua.IsSome then bars |> List.filter (fun e -> e <> hua.Value) else bars
    let barLeader = bars |> List.randomChoiceWith rng
    let message =
        match hua with
        | Some v ->
            $"你是吧主，脚滑人是 {v.Player.ToInGameString ()}"
        | None ->
            "你是吧主，本局没有脚滑人"
    sendMessage { Type = ToPlayer barLeader.Player ; Content = message }
    
    // 脚滑人知道一组身份
    
    monad {
        let! hua = hua
        let bars = bars |> List.filter (fun e -> e |> Entity.getCharaType <> JiaoHua)
        let booms = game.Entities |> List.filter (fun e -> e |> Entity.getCamp = Boom)
        let bar = bars |> List.randomChoiceWith rng
        let boom = booms |> List.randomChoiceWith rng
        let msg = $"本局有{(bar |> Entity.getCharaType).ToString()}和{(boom |> Entity.getCharaType).ToString()}"
        sendMessage { Type = ToPlayer hua.Player ; Content = msg }
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
       sendMessage { Type = ToPlayer xian.Player ; Content = msg }
    } |> ignore
    
let gameUpdate (game: GameState) = monad {
    let! main = State.get
    let status, (main, gc) =
        match game.Status with
        | Start ->
           do gameInit main game.Context
           main.Players |> List.map (fun p -> p.Id) |> NightContext.New |> Night, (main, game.Context)
        | Night night -> State.run (nightUpdate night) (main, game.Context)
        | _ -> raise (Reboot |> CommandEx)
    
    let game = { game with Context = gc ; Status = status }
    do! State.put main
    game |> Game
}