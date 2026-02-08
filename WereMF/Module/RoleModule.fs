module WereMF.Module.Role

open System
open WereMF.Common
open WereMF.State

//----------------------------------------------------------------------------
// interface

type IRoleQueriedName =
    abstract member Get : unit -> string

type IRoleQueriedHandler =
    abstract member Get : unit -> RoleHandler
    
type IRoleUpdateOnNightStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnNightEnd =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDayStart =
    abstract member Update : unit -> IRole
    
type IRoleUpdateOnDead =
    abstract member Update : unit -> IRole
    
let updateOnNightStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnNightStart as h -> h.Update ()
    | _ -> role
    
let updateOnNightEnd (role : IRole) =
    match role with
    | :? IRoleUpdateOnNightEnd as h -> h.Update ()
    | _ -> role
    
let updateOnDayStart (role : IRole) =
    match role with
    | :? IRoleUpdateOnDayStart as h -> h.Update ()
    | _ -> role

let updateOnDead (role :IRole) =
    match role with
    | :? IRoleUpdateOnDead as h -> h.Update ()
    | _ -> role

let private createSubHandler<'T when 'T :> IRole>
    (getter: 'T -> IRole) (setter: 'T -> IRole -> 'T) (subRole: IRole)  =
    let copiedHandler = match subRole with
                        | :? IRoleQueriedHandler as h -> h.Get()
                        | _ -> RoleHandler.Default
    {
        Getter = function
            | :? 'T as k -> getter k
            | value -> value
        Setter = fun r owner ->
            match owner with
            | :? 'T as k ->
                r |> copiedHandler.Setter subRole |> setter k :> IRole
            | _ -> owner
    }

//---------------------------------------------------------------------------
// functions

let getCharaType (role :IRole) =
    role.Base.CharaType
    
let getPriority (role :IRole) =
    role.Base.Priority
    
let getSummaryName (role :IRole) =
    role.Base.SummaryName

//---------------------------------------------------------------------------
// define

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
        LastSelected : PlayerId list
    }
    static member New () = { LastSelected = [] }
    interface IRole with
        member this.Base = {
            CharaType = Doge
            Priority = 10
            SummaryName = Doge.ToString ()
        }
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            { this with LastSelected = [] }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = [] }

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
        LastSelected : PlayerId list
    }
    static member New () = { LastSelected = [] }
    interface IRole with
        member this.Base = {
            CharaType = SheLang
            Priority = 4
            SummaryName = SheLang.ToString ()
        }
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            { this with LastSelected = [] }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = [] }

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
    interface IRoleQueriedName with
        member this.Get () =
            match this.CopiedRole with
            | Some role -> $"{(role |> getCharaType).ToString ()}{Kirby.ToString ()}"
            | None -> Kirby.ToString ()
    interface IRoleQueriedHandler with
        member this.Get () =
            match this.CopiedRole with
            | Some role ->
               role |> createSubHandler
                   (fun k -> k.CopiedRole.Value)
                   (fun k v -> { k with CopiedRole = Some v })
            | None -> RoleHandler.Default
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightEnd
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with CopiedRole = None }

type FenXiaRole =
    {
        FenCount : int
        CopiedRole : IRole list
        Reborn : bool
    }
    static member New () = { FenCount = 3 ; CopiedRole = [] ; Reborn = false }
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRole = this.CopiedRole |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = FenXia.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnNightStart
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnNightEnd
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
        LastSelected : PlayerId list
        Broadcasted : bool
        Exposed : bool
    }
    static member New () = { LastSelected = [] ; Broadcasted = false ; Exposed = false }
    interface IRole with
        member this.Base = {
            CharaType = ShiWu
            Priority = 7
            SummaryName = ShiWu.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Exposed = false }
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            { this with LastSelected = [] }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = [] }
        
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
        Disabled : bool
    }
    static member New count = { DiscCount = count ; Disabled = false }
    interface IRole with
        member this.Base = {
            CharaType = YinMo
            Priority = 2
            SummaryName = YinMo.ToString ()
        }
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            { this with Disabled = false }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = false }
        
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
            Priority = 100
            SummaryName = HeChong.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get () =
            match this.CopiedRole with
            | Some role ->
               role |> createSubHandler
                   (fun k -> k.CopiedRole.Value)
                   (fun k v -> { k with CopiedRole = Some v })
            | None -> RoleHandler.Default
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightEnd
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
        Reborn : bool
        BugBlocked : bool
    }
    static member New () = { MfaList = [] ; Reborn = false ; BugBlocked = false }
    interface IRole with
        member this.Base = {
            CharaType = XianSong
            Priority = 1
            SummaryName = XianSong.ToString ()
        }
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            { this with BugBlocked = false }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with BugBlocked = false }
        
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
    // we don't use interface here since we need rng
    member this.GetQueriedHandler (rng : Random) =
        let idx = if this.Fury then rng.Next this.Roles.Length else 0
        let role = this.Roles[idx]
        role |> createSubHandler
           (fun k -> k.Roles[idx])
           (fun k v -> { k with Roles = k.Roles |> List.updateAt idx v })
    interface IRole with
        member this.Base = {
            CharaType = Leaf
            Priority = 100
            SummaryName = this.SummaryName
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateRolesWith updateOnNightStart
    interface IRoleUpdateOnNightEnd with
        member this.Update () =
            this.UpdateRolesWith updateOnNightEnd
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
    
let getQueriedHandler (rng : Random) (role: IRole) =
    match role with
    | :? LeafRole as leaf -> leaf.GetQueriedHandler rng
    | :? IRoleQueriedHandler as handler -> handler.Get ()
    | _ -> RoleHandler.Default
    
let getQueriedCharaType (handler: RoleHandler) (role: IRole) =
    role |> handler.Getter |> getCharaType
    
let getQueriedName (handler: RoleHandler) (role: IRole) =
    match role with
    | :? IRoleQueriedName as name -> name.Get ()
    | _ -> (role |> handler.Getter |> getCharaType).ToString ()