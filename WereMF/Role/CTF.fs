module WereMF.Role.CTF

open WereMF.Common

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
