module WereMF.Module.Role

open System
open FSharpPlus
open WereMF.Common
open WereMF.Module.Cli
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
    
// pending

type IRolePendingHandlers =
    abstract member Get : Player -> Result<RoleHandler list, CommandType> // kirby needs input

let getPendingHandlers (player: Player) (role: IRole) =
    match role with
    | :? IRolePendingHandlers as handler -> handler.Get player
    | _ -> Ok [IdHandler]
    
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

let private createSubFunctor<'T when 'T :> IRole>
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

//---------------------------------------------------------------------------
// define

type SelectionState =
    | SelectionState of NightRecord<PlayerId list>
    static member New () =
        SelectionState { LastNight = [] ; Tonight = [] }
    member private this.Selected =
        match this with
        | SelectionState { Tonight = t ; LastNight = l } -> t @ l
    member this.Has id =
        this.Selected |> List.contains id
    member this.Add id =
        match this with
        | SelectionState { Tonight = t; LastNight = l } ->
            SelectionState { Tonight = id :: t ; LastNight = l }
    member this.AddList ids =
        match this with
        | SelectionState { Tonight = t; LastNight = l } ->
            SelectionState { Tonight = ids @ t ; LastNight = l }
    member this.UpdateOnDayStart () =
        match this with
        | SelectionState { Tonight = t } ->
            SelectionState { Tonight = [] ; LastNight = t }

type CommonRole =
    | JiaoHuaRole
    | MoleRole
    | PaoXianRole
    interface IRole with
        member this.Base =
            match this with
            | JiaoHuaRole ->
                {
                    CharaType = JiaoHua
                    Priority = 5
                    SummaryName = JiaoHua.ToString ()
                }
            | MoleRole ->
                {
                    CharaType = Mole
                    Priority = 0
                    SummaryName = Mole.ToString ()
                }
            | PaoXianRole ->
                {
                    CharaType = PaoXian
                    Priority = 0
                    SummaryName = PaoXian.ToString ()
                }

type DogeRole =
    {
        LastSelected : SelectionState
    }
    static member New () = { LastSelected = SelectionState.New () }
    interface IRole with
        member this.Base = {
            CharaType = Doge
            Priority = 10
            SummaryName = Doge.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }

type DoctorRole =
    {
        Capsule : int
    }
    static member New () = { Capsule = 4 }
    interface IRole with
        member this.Base = {
            CharaType = Doctor
            Priority = 0
            SummaryName = Doctor.ToString ()
        }

type RabiRole =
    {
        Round : int
    }
    static member New () = { Round = 0 }
    interface IRole with
        member this.Base = {
            CharaType = Rabi
            Priority = 0
            SummaryName = Rabi.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Round = this.Round + 1 }

type SheLangRole =
    {
        LastSelected : SelectionState
    }
    static member New () = { LastSelected = SelectionState.New () }
    interface IRole with
        member this.Base = {
            CharaType = SheLang
            Priority = 4
            SummaryName = SheLang.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }

type FaMaoRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = FaMao
            Priority = 0
            SummaryName = FaMao.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with FirstRound = true }

type KirbyRole =
    {
        CopiedRole : IRole option
    }
    static member New () = { CopiedRole = None }
    member private this.UpdateCopiedRoleWith updater =
        match this.CopiedRole with
        | Some role -> { this with CopiedRole = Some (role |> updater) }
        | None -> this
    interface IRole with
        member this.Base = {
            CharaType = Kirby
            Priority = 100
            SummaryName = Kirby.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
               (sub |> KirbyHandler).Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRolePendingHandlers with
        member this.Get player =
            match this.CopiedRole with
            | Some role ->
                let chara = role |> getCharaType
                let msg = {
                    Type = ToPlayer player
                    Content = $"是否使用复制技能（{chara.ToString()}）？（1：使用；0：放弃并使用吸入技能）"
                }
                monad {
                    let! yes = requestInputWithMessage msg parseBool
                    if yes |> not then [IdHandler] else

                    let sub = createSubFunctor
                               (fun k -> k.CopiedRole.Value)
                               (fun v k -> { k with CopiedRole = Some v })
                    return! role |> getPendingHandlers player |> Result.map (
                        fun l -> l |> List.map (fun h -> (sub |> KirbyHandler).Bind h))
                }
            | None -> Ok [IdHandler]
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with CopiedRole = None }

type FenXiaRole =
    {
        FenCount : int
        CopiedRoles : IRole list
        Reborn : bool
    }
    static member New () = { FenCount = 3 ; CopiedRoles = [] ; Reborn = false }
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRoles = this.CopiedRoles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = FenXia.ToString ()
        }
    interface IRolePendingHandlers with
        member this.Get player = monad {
            let mutable result = [IdHandler]
            for i in 0..(this.CopiedRoles.Length - 1) do
                let role = this.CopiedRoles[i]
                let! hs = role |> getPendingHandlers player
                let sub = createSubFunctor
                               (fun k -> k.CopiedRoles[i])
                               (fun v k ->
                     { k with CopiedRoles = k.CopiedRoles |> List.updateAt i v })
                result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
            result
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDead
        
type CreeperRole =
    {
        BombCount : int
        PlacedList : PlayerId list
    }
    static member New () = { BombCount = 3 ; PlacedList = [] }
    interface IRole with
        member this.Base = {
            CharaType = Creeper
            Priority = 0
            SummaryName = Creeper.ToString ()
        }
        
type ShiWuRole =
    {
        LastSelected : SelectionState
        Broadcasted : bool
        Exposed : bool
    }
    static member New () = { LastSelected = SelectionState.New () ; Broadcasted = false ; Exposed = false }
    interface IRole with
        member this.Base = {
            CharaType = ShiWu
            Priority = 7
            SummaryName = ShiWu.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Exposed = false }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }
        
type HuiKaRole =
    {
        FirstRound : bool
    }
    static member New () = { FirstRound = false }
    interface IRole with
        member this.Base = {
            CharaType = HuiKa
            Priority = 8
            SummaryName = HuiKa.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with FirstRound = true }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with FirstRound = true }
        
type YinMoRole =
    {
        DiscCount : int
        Disabled : bool option
    }
    static member New count = { DiscCount = count ; Disabled = None }
    member this.IsDisabled ()
        = this.Disabled.IsSome
    interface IRole with
        member this.Base = {
            CharaType = YinMo
            Priority = 2
            SummaryName = YinMo.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = None }
        
type CTFRole =
    {
        BugCount : int
        Reborn : bool
    }
    static member New count = { BugCount = count ; Reborn = false }
    interface IRole with
        member this.Base = {
            CharaType = CTF
            Priority = 3
            SummaryName = CTF.ToString ()
        }
        
type HeChongRole =
    {
        CopiedRole : IRole option
    }
    static member New () = { CopiedRole = None }
    member private this.UpdateCopiedRoleWith updater =
        match this.CopiedRole with
        | Some role -> { this with CopiedRole = Some (role |> updater) }
        | None -> this
    interface IRole with
        member this.Base = {
            CharaType = HeChong
            Priority = 9
            SummaryName = HeChong.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
               (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with CopiedRole = None }

type CaiMonRole =
    {
        CaiCount : int
        Reborn : bool
        RebornList : PlayerId list
    }
    static member New () = { CaiCount = 3 ; Reborn = false ; RebornList = [] }
    interface IRole with
        member this.Base = {
            CharaType = CaiMon
            Priority = 100
            SummaryName = CaiMon.ToString ()
        }
        
type XianSongRole =
    {
        MfaList : PlayerId list
        Reborn : bool option
        Disabled : bool option
    }
    static member New () = { MfaList = [] ; Reborn = None ; Disabled = None }
    member this.IsDisabled () =
        this.Disabled.IsSome
    interface IRole with
        member this.Base = {
            CharaType = XianSong
            Priority = 1
            SummaryName = XianSong.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = None }
        
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
        
type MyzRole =
    {
        Revealed : bool
    }
    static member New () = { Revealed = false }
    interface IRole with
        member this.Base = {
            CharaType = Myz
            Priority = 11
            SummaryName = Myz.ToString ()
        }

type LeafRole =
    {
        Roles : IRole list
        Fury : bool
    }
    static member New (roles : IRole list) = { Roles = roles ; Fury = false }
    member private this.SummaryName =
        let selects = this.Roles
                        |> List.map getSummaryName
                        |> String.concat " "
        $"{Leaf.ToString()}（{selects}）"
    member private this.UpdateRolesWith updater =
        { this with Roles = this.Roles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = Leaf
            Priority = 100
            SummaryName = this.SummaryName
        }
    interface IRoleQueriedHandler with
        member this.Get (random : Random) =
            let idx = if this.Fury then random.Next this.Roles.Length else 0
            let role = this.Roles[idx]
            let sub = createSubFunctor
                       (fun k -> k.Roles[idx])
                       (fun v k -> { k with Roles = k.Roles |> List.updateAt idx v })
            (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
    interface IRolePendingHandlers with
        member this.Get player = monad {
            if this.Fury |> not then
                let role = this.Roles[0]
                let! hs = role |> getPendingHandlers player
                let sub = createSubFunctor
                               (fun k -> k.Roles[0])
                               (fun v k ->
                     { k with Roles = k.Roles |> List.updateAt 0 v })
                hs |> List.map (fun h -> (sub |> CommonHandler).Bind h)
            else
                let mutable result = []
                for i = 1 to this.Roles.Length - 1 do
                    let role = this.Roles[i]
                    let! hs = role |> getPendingHandlers player
                    let sub = createSubFunctor
                                   (fun k -> k.Roles[i])
                                   (fun v k ->
                         { k with Roles = k.Roles |> List.updateAt i v })
                    result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
                result
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateRolesWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            this.UpdateRolesWith updateOnDead

//---------------------------------------------------------------------------
// bind

let rec createRole (r : RollResult) chara : IRole =
    match chara with
    | JiaoHua -> JiaoHuaRole
    | Doge -> DogeRole.New ()
    | Doctor -> DoctorRole.New ()
    | Mole -> MoleRole
    | Rabi -> RabiRole.New ()
    | SheLang -> SheLangRole.New ()
    | FaMao -> FaMaoRole.New ()
    | Kirby -> KirbyRole.New ()
    | FenXia -> FenXiaRole.New ()
    | Creeper -> CreeperRole.New ()
    | PaoXian -> PaoXianRole
    | ShiWu -> ShiWuRole.New ()
    | HuiKa -> HuiKaRole.New ()
    | YinMo -> YinMoRole.New r.BoomCount
    | CTF -> CTFRole.New r.BoomCount
    | HeChong -> HeChongRole.New ()
    | CaiMon -> CaiMonRole.New ()
    | XianSong -> XianSongRole.New ()
    | JiangXian -> JiangXianRole.New ()
    | Myz -> MyzRole.New ()
    | Leaf -> LeafRole.New (r.LeafRolls |> List.map (createRole r))