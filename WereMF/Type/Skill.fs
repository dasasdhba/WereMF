module WereMF.Skill

open WereMF.Chara

type ISkill =
    abstract member OwnerType : CharaType

type Skill =
    {
        OwnerType : CharaType
    }
    interface ISkill with
        member this.OwnerType = this.OwnerType