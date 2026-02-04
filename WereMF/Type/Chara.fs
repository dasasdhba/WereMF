module WereMF.Chara

type CharaCamp =
    | Bar
    | Boom
    | Yezi
    override this.ToString() =
        match this with
        | Bar -> "吧方"
        | Boom -> "爆方"
        | Yezi -> "叶子"

type CharaType =
    | JiaoHua
    | Doge
    | Doctor
    | Mole
    | Rabi
    | SheLang
    | FaMao
    | Kirby
    | FenXia
    | Creeper
    | PaoXian
    | ShiWu
    | HuiKa
    | YinMo
    | CTF
    | HeChong
    | CaiMon
    | XianSong
    | JiangXian
    | Zombie
    | Myz
    | Leaf
    override this.ToString() =
        match this with
        | JiaoHua -> "脚滑人"
        | Doge -> "Doge"
        | Doctor -> "庸医"
        | Mole -> "地鼠"
        | Rabi -> "兔子"
        | SheLang -> "铯郎"
        | FaMao -> "法猫"
        | Kirby -> "卡比"
        | FenXia -> "粉侠"
        | Creeper -> "爬行者"
        | PaoXian -> "炮仙"
        | ShiWu -> "实物"
        | HuiKa -> "灰卡比"
        | YinMo -> "音魔"
        | CTF -> "CTF"
        | HeChong -> "合虫"
        | CaiMon -> "彩怪"
        | XianSong -> "贤松"
        | JiangXian -> "江仙"
        | Zombie -> "傀儡"
        | Myz -> "myz"
        | Leaf -> "叶子"
    static member Create (name : string) =
        match name.ToLower() with
        | "脚滑人" | "脚滑" | "jiaohua" | "wsw" -> Ok JiaoHua
        | "doge" | "大爷" -> Ok Doge
        | "庸医" | "doctor" -> Ok Doctor
        | "地鼠" | "mole" -> Ok Mole
        | "兔子" | "rabi" | "rabbit" -> Ok Rabi
        | "铯郎" | "hjm" | "spring" -> Ok SheLang
        | "法猫" | "famao" | "cat" -> Ok FaMao
        | "卡比" | "kirby" | "kabi" -> Ok Kirby
        | "粉侠" | "fenxia" | "sf" -> Ok FenXia
        | "爬行者" | "爬行" | "creeper" | "tnt" -> Ok Creeper
        | "炮仙" | "pao" | "paoxian" -> Ok PaoXian
        | "实物" | "shiwu" | "250" -> Ok ShiWu
        | "灰卡比" | "灰卡" | "huika" | "huikabi"  -> Ok HuiKa
        | "音魔" | "yinmo" -> Ok YinMo
        | "ctf" -> Ok CTF
        | "合虫" | "虫合" | "hechong" | "chonghe" -> Ok HeChong
        | "彩怪" | "彩条" | "cai" | "caiguai" | "caitiao" -> Ok CaiMon
        | "贤松" | "闲松" | "xiansong" | "xian" -> Ok XianSong
        | "江仙" | "临江" | "jiangxian" | "jiang" -> Ok JiangXian
        | "傀儡" | "kuilei" -> Ok Zombie
        | "myz" -> Ok Myz
        | "叶子" | "yezi" | "leaf" -> Ok Leaf
        | _ -> Error $"无效的身份: {name}"
    member this.GetCamp() : CharaCamp =
        match this with
        | JiaoHua | Doge | Doctor | Mole | Rabi | SheLang | FaMao | Kirby | FenXia | Creeper -> Bar
        | PaoXian | ShiWu | HuiKa | YinMo | CTF | HeChong | CaiMon | XianSong | JiangXian | Zombie | Myz -> Boom
        | Leaf -> Yezi