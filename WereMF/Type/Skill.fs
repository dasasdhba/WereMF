module WereMF.Skill

open WereMF.Chara
open WereMF.Entity

type KillType =
    | Death
    | SuddenDeath
    | ForceDeath

type SpringType =
    | Once
    | Recursed

type SkillBuilder = {
    OwnerType : CharaType
    FromKirby : bool
    Source : Entity
    Priority : int
}

type ISkill =
    abstract member GetOwnerType : unit -> CharaType
    abstract member GetKillType : unit -> KillType option