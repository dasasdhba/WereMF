namespace WereMF.Common

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

type ThreatenType =
    | ThreatenSkill
    | ThreatenVote of target : PlayerId * force : bool

type ThreatenState = {
    Type : ThreatenType
    Source : PlayerId
}

type MilkState = {
    Tonight : bool
    LastNight : bool
}

type EntityState =
    {
        BarLeader : bool option // 吧主票
        PaoXianParty : PlayerId list // 炮仙队友，由于复制的炮仙不能知道队友，所以写这里
        Reversed : bool // 法猫反转
        
        Dead : DeadState
        Reborn : RebornState option // 彩怪
        Smog : int list // 灰卡比
        Bug : int // ctf
        Capsule : int list // 庸医
        Potion : int list // 法猫
        XianSong : int // 闲松球
        Kidnapped : PlayerId option // 实物
        Threaten : ThreatenState option // myz
        Bomb : int // creeper
        Milk : MilkState
        
        JiaoHuaVoteBlocked : bool // 脚滑人禁票
        JiaoHuaProtected : bool // 脚滑人保护
        JiaoHuaBlocked : int // 脚滑人封技能
        
        LeafProtected : bool // 叶子不可选中
    }
    member this.SmogCount = this.Smog.Length
    member this.CapsuleCount = this.Capsule.Length
    member this.PotionCount = this.Potion.Length
    static member New () =
        {
            BarLeader = None
            PaoXianParty = []
            Reversed = false
            Dead = { Dead = false ; Name = "" }
            Reborn = None
            Smog = []
            Bug = 0
            Capsule = []
            Potion = []
            XianSong = 0
            Kidnapped = None
            Threaten = None
            Bomb = 0
            Milk = { Tonight = false ; LastNight = false }
            JiaoHuaVoteBlocked = false
            JiaoHuaProtected = false
            JiaoHuaBlocked = 0
            LeafProtected = false
        }

type Entity =
    {
        Player : Player
        Role : IRole
        State : EntityState
    }