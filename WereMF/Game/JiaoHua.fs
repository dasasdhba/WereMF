module WereMF.Game.JiaoHua

open FSharpPlus
open FSharpPlus.Data
open WereMF.Cli
open WereMF.Entity
open WereMF.Player
open WereMF.Skill
open WereMF.Chara
open WereMF.Game.Handler
open WereMF.Role

type JiaoHuaRole () =
    interface IRole with
        member this.GetCharaType() = JiaoHua
        member this.GetPriority() = 5
        member this.GetCopiedRole() = this
        member this.GetQueriedChara() = JiaoHua
        member this.GetSummaryCharaName() = JiaoHua.ToString()
        
let createJiaoHuaSkill source target : Skill =
    {
        OwnerType = JiaoHua
        KillType = None
        SpringType = None
        FromKirby = false
        Source = source
        Target = target
    }

type JiaoHuaHandler(sender : Entity) =
    interface ISkillHandler with
        member this.Send night = monad {
            let! current = State.get
            let parser input =
                let p = parsePlayerId input current
                match p with
                | Ok pId ->
                    if pId = sender.Player.Id then
                        Error "你不能查自己"
                    else
                        Ok pId
                | Error e -> Error e
            let msg = { Type = ToPlayer sender.Player ; Content = "输入一个玩家以查询身份，输入 0 放弃" }
            let! current, result = requestInputWithMessage msg parser
            do! State.put current
            return sendSkill current createJiaoHuaSkill sender result 0
        }