module WereMF.Game.Handler

open WereMF.State.NightState

type SendType =
    | Continue
    | Next of ActionData list option

type ISkillHandler =
    abstract member Send : unit -> SendType