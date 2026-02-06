module WereMF.Game.Bind

open WereMF.Entity
open WereMF.Chara
open WereMF.Game.JiaoHua
open WereMF.Game.Leaf
open WereMF.Game.PaoXian
open WereMF.Role
open WereMF.Game.Handler

let createRole (chara : CharaType) : IRole option =
    match chara with
    | JiaoHua -> Some (JiaoHuaRole () :> IRole)
    | PaoXian -> Some (PaoXianRole () :> IRole)
    | _ -> None
    
let createLeafRole (roles : CharaType list) : LeafRole =
    {
        Roles = roles |> List.fold (fun acc chara ->
                    let r = createRole chara
                    match r with
                    | Some r -> r :: acc
                    | None -> acc
                ) []
        Fury = false
    }

let createHandler (entity : Entity) : ISkillHandler option =
    match entity.Role with
    | :? JiaoHuaRole -> Some (JiaoHuaHandler entity :> ISkillHandler)
    | :? PaoXianRole -> Some (PaoXianHandler entity :> ISkillHandler)
    | _ -> None