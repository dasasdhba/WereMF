namespace WereMF.Common

open FSharp.Data

type DeadState = {
    Dead : bool
    Name : string
}

type RebornState =
    {
        ReadyRound : int
        RebornRound : int
    }
    member this.Reborn = this.ReadyRound <= 0 && this.RebornRound > 0

type NightRecord<'T> =
    {
        Tonight : 'T
        LastNight : 'T
    }

type SelectionState =
    | SelectionState of NightRecord<PlayerId list>
    static member New () =
        SelectionState { LastNight = [] ; Tonight = [] }
    member this.Selected =
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
    member this.ToJsonValue () =
        match this with
        | SelectionState { Tonight = t ; LastNight = l } ->
            JsonValue.Record [|
                "tonight", t |> List.mapJson (fun p -> p.ToJsonValue())
                "last_night", l |> List.mapJson (fun p -> p.ToJsonValue())
            |]

type MilkState =
    | MilkState of NightRecord<bool>
    static member New () =
        MilkState { Tonight = false ; LastNight = false }
    member this.HasLastMilk =
        match this with
        | MilkState { LastNight = true } -> true
        | _ -> false
    member this.Set () =
        match this with
        | MilkState { LastNight = l } -> MilkState { Tonight = true ; LastNight = l }
    member this.UpdateOnDayStart () =
        match this with
        | MilkState { Tonight = v } -> MilkState { Tonight = false ; LastNight = v }

type EntityState =
    {
        BarLeader : bool option // 吧主票
        PaoXianParty : PlayerId list // 炮仙队友，由于复制的炮仙不能知道队友，所以写这里
        Reversed : bool // 法猫反转
        
        Dead : DeadState
        Reborn : RebornState option // 彩怪
        Smog : int list // 灰卡比
        Bug : int option // ctf
        Capsule : int list // 庸医
        Potion : int list // 法猫
        XianSong : int // 闲松球
        Kidnapped : PlayerId list // 实物
        Threaten : bool option // myz
        QueuedBomb : int // creeper
        Bomb: int
        Milk : MilkState
        
        JiaoHuaVoteBlocked : bool // 脚滑人禁票
        JiaoHuaProtected : bool // 脚滑人保护
        JiaoHuaBlocked : int // 脚滑人封技能
        
        LeafProtected : bool option // 叶子不可选中
    }
    member this.SmogCount = this.Smog.Length
    member this.CapsuleCount = this.Capsule.Length
    member this.PotionCount = this.Potion.Length
    member this.BugCount = if this.Bug.IsSome then this.Bug.Value else 0
    member this.ToJsonValue (showMyzThreaten: bool) =
        JsonValue.Record [|
            "is_bar_leader", JsonValue.Boolean this.BarLeader.IsSome
            "is_dead", JsonValue.Boolean this.Dead.Dead
            "is_dead_public", JsonValue.Boolean (this.Dead.Name <> "")
            "dead_showing_name", JsonValue.String this.Dead.Name
            "reversed", JsonValue.Boolean this.Reversed
            "smog_count", JsonValue.Number (decimal this.SmogCount)
            "capsule_count", JsonValue.Number (decimal this.CapsuleCount)
            "potion_count", JsonValue.Number (decimal this.PotionCount)
            "xian_song_count", JsonValue.Number (decimal this.XianSong)
            "bug_count", JsonValue.Number (decimal this.BugCount)
            "myz_threaten", JsonValue.Boolean (showMyzThreaten && this.Threaten.IsSome)
            "jiaohua_vote_blocked", JsonValue.Boolean this.JiaoHuaVoteBlocked
            "shiwu_kidnapped", JsonValue.Boolean (this.Kidnapped.IsEmpty |> not)
            "jiaohua_protected", JsonValue.Boolean this.JiaoHuaProtected
            "jiaohua_blocked", JsonValue.Number (decimal this.JiaoHuaBlocked)
            "leaf_protected", JsonValue.Boolean this.LeafProtected.IsSome
        |]
    member this.PublicChangesFrom (before: EntityState) =
        let fields = function
            | JsonValue.Record values -> values
            | _ -> [||]
        let beforeFields = before.ToJsonValue false |> fields
        let afterFields = this.ToJsonValue false |> fields
        let changes = afterFields |> Array.choose (fun (name, value) ->
            match beforeFields |> Array.tryFind (fun (beforeName, _) -> beforeName = name) with
            | Some (_, beforeValue) when beforeValue = value -> None
            | _ -> Some (name, value))
        if Array.isEmpty changes then None else Some (JsonValue.Record changes)
    member this.ToJsonValue () = this.ToJsonValue true
    static member New () =
        {
            BarLeader = None
            PaoXianParty = []
            Reversed = false
            Dead = { Dead = false ; Name = "" }
            Reborn = None
            Smog = []
            Bug = None
            Capsule = []
            Potion = []
            XianSong = 0
            Kidnapped = []
            Threaten = None
            QueuedBomb = 0
            Bomb = 0
            Milk = MilkState.New ()
            JiaoHuaVoteBlocked = false
            JiaoHuaProtected = false
            JiaoHuaBlocked = 0
            LeafProtected = None
        }

type Entity =
    {
        Player : Player
        Role : IRole
        State : EntityState
    }
    member this.ToJsonValue (showMyzThreaten: bool) =
        JsonValue.Record [|
            "player", this.Player.ToJsonValue ()
            "role", JsonValue.Record [|
                "chara_type", this.Role.Base.CharaType.ToJsonValue ()
                "summary_name", JsonValue.String this.Role.Base.SummaryName
                "data", this.Role.ToJsonValue ()
            |]
            "state", this.State.ToJsonValue showMyzThreaten
        |]
    member this.ToJsonValue () = this.ToJsonValue true
    
type DeadType =
    | Kill
    | Sudden
    | Force
    | Vote
    
type DeadRequest =
    {
        DeadType : DeadType
        GetName : Entity -> string
        GetReveal : string -> Entity -> string
    }
    static member New t =
        {
            DeadType = t
            GetName = fun e -> e.Player.Name
            GetReveal = fun name e -> $"{e.Player.Name}是{name}"
        }
    static member FromSelf header t =
        {
            DeadType = t
            GetName = fun e -> header
            GetReveal = (fun name e ->
                    let result = $"这个{header}是{e.Player.Name}"
                    if header = name then result
                    else $"{result}，{e.Player.Name}是{name}"
                )
        }
