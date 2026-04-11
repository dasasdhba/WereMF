namespace WereMF.Common

open FSharp.Data

type RoleBase =
    {
        CharaType : CharaType
        Priority : int
        SummaryName : string
    }

type IRole =
    abstract member Base : RoleBase
    abstract member ToJsonValue : unit -> JsonValue