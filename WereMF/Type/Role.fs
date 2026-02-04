module WereMF.Role

open WereMF.Chara

type IRole =
    abstract member Type : CharaType

type Role =
    {
        Type : CharaType
    }