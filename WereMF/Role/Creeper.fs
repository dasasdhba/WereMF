module WereMF.Role.Creeper

open WereMF.Common

type CreeperRole =
    {
        BombCount : int
        PlacedList : PlayerId list
    }
    static member New () = { BombCount = 3 ; PlacedList = [] }
    interface IRole with
        member this.Base = {
            CharaType = Creeper
            Priority = 0
            SummaryName = Creeper.ToString ()
        }
