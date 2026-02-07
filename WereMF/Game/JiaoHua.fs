module WereMF.Game.JiaoHua

open WereMF.Game.Send
open WereMF.Type.Skill
open WereMF.Type.Chara
open WereMF.Type.Role

type JiaoHuaRole () =
    inherit Role()
    override this.GetCharaType() = JiaoHua
    override this.GetPriority() = 5

let sendJiaoHuaSkill (s : PendingSkill) =
    s |> sendNonSelfSkill {
        InputHint = "输入一名玩家的编号查询其身份，输入 0 以放弃"
        SelfHint = "你不能查自己"
        Type = Some JiaoHua
    }
