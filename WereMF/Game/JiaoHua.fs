module WereMF.Game.JiaoHua

open FSharpPlus
open FSharpPlus.Data
open WereMF.Game.Cli
open WereMF.Type.Entity
open WereMF.Type.Player
open WereMF.Type.Skill
open WereMF.Type.Chara
open WereMF.Game.Handler
open WereMF.Type.Role

type JiaoHuaRole () =
    interface IRole with
        member this.GetCharaType() = JiaoHua
        member this.GetPriority() = 5
        member this.GetCopiedRole() = this
        member this.GetQueriedChara() = JiaoHua
        member this.GetSummaryCharaName() = JiaoHua.ToString()

type JiaoHuaHandler(sender : Entity) =
    interface ISkillHandler with
        member this.Send() = Continue