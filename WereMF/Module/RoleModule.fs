module WereMF.Module.Role

open System
open WereMF.Common

//----------------------------------------------------------------------------
// interface

// query

type IRoleQueriedHandler =
    abstract member Get : Random -> RoleHandler // leaf needs rng

let getQueriedHandler (rng : Random) (role: IRole) =
    match role with
    | :? IRoleQueriedHandler as handler -> handler.Get rng
    | _ -> IdHandler
    
// pending & valid

type IRolePendingHandlers =
    abstract member Get : Player -> RoleHandler list // kirby needs input

type IRoleValidHandlers =
    abstract member Get : unit -> RoleHandler list

let getPendingHandlers (player: Player) (role: IRole) =
    match role with
    | :? IRolePendingHandlers as handler -> handler.Get player
    | _ -> [IdHandler]
    
let getValidHandlers (role: IRole) =
    match role with
    | :? IRoleValidHandlers as handler -> handler.Get ()
    | _ -> [IdHandler]
    
// in game update
    
type IRoleUpdateOnNightStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDayStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDead =
    abstract member Update : unit -> IRole
    
let updateOnNightStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnNightStart as h -> h.Update ()
    | _ -> role
    
let updateOnDayStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnDayStart as h -> h.Update ()
    | _ -> role

let updateOnDead (role :IRole) =
    match role with
    | :? IRoleUpdateOnDead as h -> h.Update ()
    | _ -> role

//---------------------------------------------------------------------------
// functions

let createSubFunctor<'T when 'T :> IRole>
    (getter: 'T -> IRole) (setter: IRole -> 'T -> 'T) =
    {
        Getter = function
            | :? 'T as k -> getter k
            | value -> value
        Setter = fun r owner ->
            match owner with
            | :? 'T as k ->
                k |> setter r :> IRole
            | _ -> owner
    }

let getCharaType (role :IRole) =
    role.Base.CharaType
    
let getPriority (role :IRole) =
    role.Base.Priority
    
let getSummaryName (role :IRole) =
    role.Base.SummaryName

let getQueriedCharaType (handler: RoleHandler) (role: IRole) =
    role |> handler.Getter |> getCharaType
    
let getQueriedName (handler: RoleHandler) (role: IRole) =
    let chara = getQueriedCharaType handler role
    match handler with
    | KirbyHandler _ -> $"{chara.ToString()}{Kirby.ToString()}"
    | _ -> chara.ToString()
