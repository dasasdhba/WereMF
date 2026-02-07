module WereMF.Game.Bind

open FSharpPlus
open WereMF.Type.Chara
open WereMF.Game.JiaoHua
open WereMF.Game.Leaf
open WereMF.Game.PaoXian
open WereMF.Type.Role

let createRole (chara : CharaType) : Role option = monad {
    match chara with
    | JiaoHua -> JiaoHuaRole ()
    | PaoXian -> PaoXianRole ()
    | _ -> return! None
}

let createLeafRole (roles : CharaType list) : LeafRole =
    let roles = roles |> List.map createRole |> List.choose id
    LeafRole roles