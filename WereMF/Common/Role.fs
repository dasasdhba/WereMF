namespace WereMF.Common

type RoleBase =
    {
        CharaType : CharaType
        Priority : int
        SummaryName : string
    }

type IRole =
    abstract member Base : RoleBase