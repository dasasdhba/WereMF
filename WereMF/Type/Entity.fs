module WereMF.Type.Entity

open WereMF.Type.Chara
open WereMF.Type.Player
open WereMF.Type.Role

type DeadState = {
    Dead : bool
    Name : string
}

type RebornState = {
    ReadyRound : int
    RebornRound : int
}

type ThreatenType =
    | ThreatenNight of IRole
    | ThreatenDay

type ThreatenState = {
    Type : ThreatenType
    Target : PlayerId
    Force : bool
}

type EntityState =
    {
        BarLeader : bool // 吧主票
        Reversed : bool // 法猫反转
        
        Dead : DeadState
        Reborn : RebornState option // 粉侠 / 彩怪
        Smog : int list // 灰卡比
        Bug : int // ctf
        Capsule : int list // 庸医
        Potion : int list // 法猫
        XianSong : int // 闲松球
        Kidnapped : bool // 实物
        Threaten : ThreatenState option // myz
        Bomb : bool // creeper
        
        JiaoHuaVoteBlocked : bool // 脚滑人禁票
        JiaoHuaProtected : bool // 脚滑人保护
        JiaoHuaBlocked : IRole list // 脚滑人封技能
        
        LeafProtected : bool // 叶子不可选中
    }
    
let newEntityState = {
    BarLeader = false
    Reversed = false
    Dead = { Dead = false ; Name = "" }
    Reborn = None
    Smog = []
    Bug = 0
    Capsule = []
    Potion = []
    XianSong = 0
    Kidnapped = false
    Threaten = None
    Bomb = false
    JiaoHuaVoteBlocked = false
    JiaoHuaProtected = false
    JiaoHuaBlocked = []
    LeafProtected = false
}

type Entity =
    {
        Player : Player
        Role : IRole
        State : EntityState
    }
    member this.SetState(state) =
        { this with State = state }
    member this.SetRole(role) =
        { this with Role = role }
        
    member this.BarLeader = this.State.BarLeader
    member this.SetBarLeader value =
        this.SetState { this.State with BarLeader = value }
        
    member this.Reversed = this.State.Reversed
    member this.SetReversed value =
        this.SetState { this.State with Reversed = value }
        
    member this.Dead = this.State.Dead
    member this.SetDead value =
        this.SetState { this.State with Dead = value }
        
    member this.Reborn = this.State.Reborn
    member this.SetReborn round =
        this.SetState { this.State with Reborn = Some { ReadyRound = round; RebornRound = round } }
    
    member this.Smog = this.State.Smog.Length
    member this.AddSmog round =
        this.SetState { this.State with Smog = round :: this.State.Smog }
    member this.ClearSmog () =
        this.SetState { this.State with Smog = [] }
        
    member this.Bug = this.State.Bug
    member this.AddBug count =
        this.SetState { this.State with Bug = this.Bug + count }
    member this.SubBug count = this.AddBug (max -count this.Bug)
    member this.ClearBug ()  = this.SetState { this.State with Bug = 0 }
    
    member this.Capsule = this.State.Capsule.Length
    member this.AddCapsule round =
        this.SetState { this.State with Capsule = round :: this.State.Capsule }
    member this.ClearCapsule () =
        this.SetState { this.State with Capsule = [] }
    
    member this.Potion = this.State.Potion.Length
    member this.AddPotion round =
        this.SetState { this.State with Potion = round :: this.State.Potion }
    member this.ClearPotion () =
        this.SetState { this.State with Potion = [] }
        
    member this.XianSong = this.State.XianSong
    member this.AddXianSong count =
        this.SetState { this.State with XianSong = this.XianSong + count }
    member this.SubXianSong count = this.AddXianSong (max -count this.XianSong)
    member this.ClearXianSong () =
        this.SetState { this.State with XianSong = 0 }
        
    member this.Kidnapped = this.State.Kidnapped
    member this.SetKidnapped value =
        this.SetState { this.State with Kidnapped = value }
        
    member this.Threaten = this.State.Threaten
    member this.SetThreaten value =
        this.SetState { this.State with Threaten = value }
        
    member this.Bomb = this.State.Bomb
    member this.SetBomb value =
        this.SetState { this.State with Bomb = value }
    
    member this.JiaoHuaVoteBlocked = this.State.JiaoHuaVoteBlocked
    member this.SetJiaoHuaVoteBlocked value =
        this.SetState { this.State with JiaoHuaVoteBlocked = value }
    member this.JiaoHuaProtected = this.State.JiaoHuaProtected
    member this.SetJiaoHuaProtected value =
        this.SetState { this.State with JiaoHuaProtected = value }
    member this.JiaoHuaBlocked = this.State.JiaoHuaBlocked
    member this.AddJiaoHuaBlocked role =
        this.SetState { this.State with JiaoHuaBlocked = role :: this.State.JiaoHuaBlocked }
    member this.ClearJiaoHuaBlocked () =
        this.SetState { this.State with JiaoHuaBlocked = [] }
        
    member this.LeafProtected = this.State.LeafProtected
    member this.SetLeafProtected value =
        this.SetState { this.State with LeafProtected = value }
        
    member this.CanBeSelected () =
        not (this.State.JiaoHuaProtected || this.State.LeafProtected)
    member this.CanVote () =
        not this.State.JiaoHuaVoteBlocked
        
    member this.GetCamp() =
        let camp = this.Role.GetCharaType().GetCamp ()
        if not this.Reversed then
            camp
        else
            match camp with
            | Bar -> Boom
            | Boom -> Bar
            | _ -> Yezi
            
    member this.ClearMarks() =
        let r = this.ClearSmog ()
        let r = r.ClearBug ()
        let r = r.ClearCapsule ()
        let r = r.ClearPotion ()
        let r = r.ClearXianSong ()
        r
        
    member this.GetCopiedRole() =
        if this.Smog > 0 then
            None
        else
            Some (this.Role.GetCopiedRole())
        
    member this.GetQueriedChara() =
        if this.Smog > 0 then
            None
        else
            Some (this.Role.GetQueriedChara())
        
    member this.GetInGameName() =
        let reversed = if this.Reversed then "反·" else ""
        let reborn = match this.Reborn with
                     | Some _ -> "（复活）"
                     | None -> ""
        reversed + this.Player.Name + reborn
        
    member this.GetDeadName() =
        match this.GetQueriedChara() with
        | Some chara ->
            let reversed = if this.Reversed then "反·" else ""
            reversed + chara.ToString()
        | None -> "???"
        
    member this.GetSummaryName() =
        let reversed = if this.Reversed then "反·" else ""
        reversed + this.Role.GetSummaryCharaName()
        
    member this.GetTopMark() =
        let voteBlock = if this.JiaoHuaVoteBlocked then "\u2716" else "️"
        let protect = if this.JiaoHuaProtected then "\U0001F6E1" else "️"
        let roleBlock = if this.JiaoHuaBlocked.Length > 0 then "\u274c" else "️"
        let leafBlock = if this.LeafProtected then "\u274e" else "️"
        voteBlock + protect + roleBlock + leafBlock
        
    member this.GetBuffMark() =
        let repeat n s =
            if n <= 0 then
                ""
            elif n = 1 then
                s
            else
                [ for i in 1 .. n -> s ] |> String.concat ""
        let smog = repeat this.Smog "\u2601"
        let bug = repeat this.Bug "\U0001F41E"
        let xian = repeat this.XianSong "\U0001F36A"
        let cap = repeat this.Capsule "\U0001F48A"
        let drop = repeat this.Potion "\U0001F4A7"
        smog + bug + xian + cap + drop
        
    member this.GetNightSummary() =
        if this.Dead.Dead then
            $"【{this.Dead.Name}】"
        else
            this.Player.Id.ToCircleString() + " " + this.GetInGameName() + " "
            + this.GetTopMark() + " " + this.GetBuffMark()

    member this.GetDaySummary() =
        if this.Dead.Dead then
            $"【{this.Dead.Name}】"
        else
            this.Player.Id.ToCircleString() + " " + this.GetInGameName() + " "
            + this.GetTopMark()
        
    member this.GetSummary() =
        this.Player.ToAliveString() + ": " + this.GetSummaryName()