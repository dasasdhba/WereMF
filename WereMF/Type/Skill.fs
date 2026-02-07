module WereMF.Type.Skill

open WereMF.Type.Chara
open WereMF.Type.Player
open WereMF.Type.Role

type PendingSkill = {
    Role : Role
    Source : PlayerId
    Priority : int
    Kidnapped : bool
    Blocked : bool
    FromKirby : bool
}

type Skill = {
    Type : CharaType
    Source : PlayerId
    Target : PlayerId
    FromKirby : bool
}