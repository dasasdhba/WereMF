module WereMF.Type.Role

open WereMF.Type.Chara

type IRole =
    abstract member GetCharaType : unit -> CharaType
    abstract member GetPriority : unit -> int
    abstract member GetCopiedRole : unit -> IRole
    abstract member GetQueriedChara : unit -> CharaType
    abstract member GetSummaryCharaName : unit -> string