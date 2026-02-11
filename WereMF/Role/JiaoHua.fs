module WereMF.Role.JiaoHua

open WereMF.Common
open WereMF.Module.Role

type JiaoHuaRole =
    {
        VoteBlock : bool
    }
    static member New () = { VoteBlock = false }
    interface IRole with
        member this.Base = {
            CharaType = JiaoHua
            Priority = 5
            SummaryName = JiaoHua.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with VoteBlock = false }
    interface IRoleUpdateOnDead with
        member this.Update dead =
            if dead <> Kill then this else { this with VoteBlock = true }
