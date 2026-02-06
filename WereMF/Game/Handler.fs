module WereMF.Game.Handler

open FSharpPlus.Data
open WereMF.Player
open WereMF.Entity
open WereMF.GameState
open WereMF.NightState

type SendType =
    | Continue
    | Next of ActionData list option

type ISkillHandler =
    abstract member Send : NightState -> State<GameStack, GameStack * SendType>
    
// todo: simplify this
let sendSkill (current : GameStack) (skillCreator : Entity -> Entity -> Skill)
    (sender : Entity) (targetId : PlayerId option) (priority : int) =
    match targetId with
    | None -> current, Continue
    | Some target ->
        if target <= PlayerId 0 then
            current, Next None
        else
            let skill = skillCreator sender (current.GetEntity target)
            let data = { Skill = skill ; Priority = priority }
            current, Next (Some [data])