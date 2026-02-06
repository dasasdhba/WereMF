module WereMF.Game.PaoXian

open FSharpPlus
open FSharpPlus.Data
open WereMF.Cli
open WereMF.Entity
open WereMF.Skill
open WereMF.Chara
open WereMF.Game.Handler
open WereMF.Role

type PaoXianRole () =
    interface IRole with
        member this.GetCharaType() = PaoXian
        member this.GetPriority() = 0
        member this.GetCopiedRole() = this
        member this.GetQueriedChara() = PaoXian
        member this.GetSummaryCharaName() = PaoXian.ToString()

let createPaoXianSkill source target : Skill =
    {
        OwnerType = PaoXian
        KillType = Some Death
        SpringType = None
        FromKirby = false
        Source = source
        Target = target
    }

type PaoXianHandler(sender : Entity) =
    interface ISkillHandler with
        member this.Send night = monad {
            let! current = State.get
            let parser input = parsePlayerId input current
            let msg = { Type = ToPlayer sender.Player ; Content = "输入一个玩家令其死亡，输入 0 放弃" }
            let! current, result = requestInputWithMessage msg parser
            do! State.put current
            return sendSkill current createPaoXianSkill sender result 0
        }