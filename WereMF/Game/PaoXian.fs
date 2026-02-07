module WereMF.Game.PaoXian

open WereMF.Game.Send
open WereMF.Type.Chara
open WereMF.Type.Role
open WereMF.Type.Skill

type PaoXianRole () =
    inherit Role()
    override this.GetCharaType() = PaoXian

let sendPaoXianSkill (s : PendingSkill) =
    s |> sendNonSelfSkill {
        InputHint = "输入一名玩家的编号令其死亡，输入 0 以放弃"
        SelfHint = "你不能杀死自己"
        Type = Some PaoXian
    }