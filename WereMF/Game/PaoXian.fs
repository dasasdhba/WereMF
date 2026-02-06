module WereMF.Game.PaoXian

open FSharpPlus
open FSharpPlus.Data
open WereMF.Game.Cli
open WereMF.Type.Entity
open WereMF.Type.Skill
open WereMF.Type.Chara
open WereMF.Game.Handler
open WereMF.Type.Role

type PaoXianRole () =
    interface IRole with
        member this.GetCharaType() = PaoXian
        member this.GetPriority() = 0
        member this.GetCopiedRole() = this
        member this.GetQueriedChara() = PaoXian
        member this.GetSummaryCharaName() = PaoXian.ToString()

type PaoXianHandler(sender : Entity) =
    interface ISkillHandler with
        member this.Send() = Continue