module WereMF.Role.CaiMon

open WereMF.Common

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
