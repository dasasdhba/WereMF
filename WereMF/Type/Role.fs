module WereMF.Type.Role

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Type.Chara

[<AbstractClass>]
type Role () =
    abstract member GetCharaType : unit -> CharaType
    abstract member GetPriority : unit -> int
    abstract member GetRabiRole : unit -> Reader<Random, Role>
    abstract member GetCopiedRole : unit -> Reader<Random, Role>
    abstract member GetQueriedChara : unit -> Reader<Random, CharaType>
    abstract member GetQueriedCharaName : unit -> Reader<Random, string>
    abstract member GetSummaryCharaName : unit -> string
    default this.GetPriority() = 0
    default this.GetRabiRole() = monad { this }
    default this.GetCopiedRole() = monad { this }
    default this.GetQueriedChara() = monad {
        let! role = this.GetCopiedRole()
        role.GetCharaType ()
    }
    default this.GetQueriedCharaName() = monad {
        let! chara = this.GetQueriedChara()
        chara.ToString ()
    }
    default this.GetSummaryCharaName () = this.GetCharaType().ToString()