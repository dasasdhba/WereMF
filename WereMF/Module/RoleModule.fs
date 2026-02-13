module WereMF.Module.Role

open System
open FSharpPlus.Data
open WereMF.Common
open WereMF.State

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

type DeadContext = Entity * BindContext

type IRolePreventDead =
    abstract member Prevent : DeadType -> RoleHandler -> State<DeadContext, bool>

let tryPreventDead (deadType: DeadType) (handler: RoleHandler) (context : DeadContext) (role: IRole) =
    match role with
    | :? IRolePreventDead as h ->
        let r, context = State.run (h.Prevent deadType handler) context
        context, r
    | _ -> context, false
    
// in game update

type IRoleGetNightStartDeadRequest =
    abstract member Get : unit -> DeadRequest list
    
type IRoleGetDayStartDeadRequest =
    abstract member Get : unit -> DeadRequest list
    
type IRoleUpdateOnNightStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDayStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDead =
    abstract member Update : DeadType -> IRole

let getNightStartDeadRequest (role : IRole) =
    match role with
    | :? IRoleGetNightStartDeadRequest as h -> h.Get ()
    | _ -> []
    
let getDayStartDeadRequest (role : IRole) =
    match role with
    | :? IRoleGetDayStartDeadRequest as h -> h.Get ()
    | _ -> []

let updateOnNightStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnNightStart as h -> h.Update ()
    | _ -> role
    
let updateOnDayStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnDayStart as h -> h.Update ()
    | _ -> role

let updateOnDead dead (role :IRole) =
    match role with
    | :? IRoleUpdateOnDead as h -> h.Update dead
    | _ -> role

// vote update

type IRoleUpdateOnVoteStart =
    abstract member Update : Entity -> State<GameContext, IRole>

type IRoleUpdateOnVoteEnd =
    abstract member Update : Entity -> State<DayContext, IRole>

// leaf specific

type IRoleLeaf =
    abstract member Fury : bool
    abstract member SetFury : unit -> IRole

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

let updateNightOptionBool bool : bool option =
    match bool with
    | Some true -> Some false
    | _ -> None
    
let updateRoleWithHandler<'T when 'T :> IRole> (updater: 'T -> 'T) (handler: RoleHandler) entity =
    let role = handler.GetFromEntity entity
    let role =
        match role with
        | :? 'T as k ->
            k |> updater :> IRole
        | _ -> failwith $"Error：Unexpected role set with {handler.GetFromEntity entity |> getCharaType}"
    handler.SetToEntity role entity
    
let getFromRoleWithHandler<'S,'T when 'T :> IRole> (getter: 'T -> 'S) (handler: RoleHandler) entity =
    let role = handler.GetFromEntity entity
    match role with
    | :? 'T as k ->
        k |> getter
    | _ -> failwith $"Error：Unexpected role get with {handler.GetFromEntity entity |> getCharaType}"