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
    let bars = game.Entities |> List.filter (fun e -> e |> Entity.getCamp = Bar)
    let hua = bars |> List.tryFind (fun e -> e.Role |> Role.getCharaType = JiaoHua)
    let bars = if hua.IsSome then bars |> List.filter (fun e -> e <> hua.Value) else bars
    let barLeader = bars |> List.randomChoiceWith rng
    let message = match hua with
                  | Some v ->
                      $"你是吧主，脚滑人是 {v.Player.ToInGameString ()}"
                  | None ->
                      "你是吧主，本局没有脚滑人"
    sendMessage { Type = ToPlayer barLeader.Player ; Content = message }
    
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