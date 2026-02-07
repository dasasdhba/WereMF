module WereMF.Game.Send

open FSharpPlus
open FSharpPlus.Data
open FSharpPlus.Data.ResultOrException
open Microsoft.FSharp.Core
open WereMF.Game.Cli
open WereMF.State.GameState
open WereMF.Type.Chara
open WereMF.Type.Player
open WereMF.Type.Skill

type NonSelfSkill = {
    InputHint : string
    SelfHint : string
    Type : CharaType option
}

let sendNonSelfSkill ds s = monad {
    if s.Kidnapped then Ok None else

    let! (game: GameContext) = Reader.ask
    let filter = function
        | p when p = s.Source -> Error ds.SelfHint
        | p when (game.GetEntity p).IsDead() -> Error "指定的玩家已死亡"
        | p when (game.GetEntity p).CanBeSelected() |> not -> Error "指定的玩家不可选中"
        | p -> Ok p
    if game.Entities |> List.exists (fun p ->
        p.Player.Id |> filter |> Result.isOk ) |> not then Ok None else
    
    let entity = game.GetEntity s.Source
    let msg = {
        Type = ToPlayer entity.Player
        Content = ds.InputHint
    }
    let parser = parsePlayerId >> (function
        | Ok p when p <= PlayerId 0 -> Ok (PlayerId 0)
        | Ok p when game.HasEntity p |> not -> Error "指定的玩家编号不存在"
        | Ok p -> filter p
        | value -> value
    )
    monad {
        let! target = requestInputWithMessage msg parser
        if target = PlayerId 0 then None else
        Some {
            Type = defaultArg ds.Type (s.Role.GetCharaType ())
            Source = s.Source
            Target = target
            FromKirby = s.FromKirby
        }
    }
}