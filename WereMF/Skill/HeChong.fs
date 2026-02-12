module WereMF.Skill.HeChong

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.HeChong

type HeChongSkill =
    | HeChongSkill
    interface ISkill

let heChongSendSkill ps (game: WereMF.State.GameContext) =
    let entity = game.GetEntity ps.Source
    let last =
        match ps.Handler.GetFromEntity entity with
        | :? HeChongRole as he -> he.LastSelected.Selected
        | _ -> []
        
    let title = "输入一名其他玩家的编号复制其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能复制自己"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList last "不能连续模仿同一个玩家"
    let filter = giveUpOrFilterWith filter
    let def () = HeChongSkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r HeChongSkill |> Some ])
    ps |> sendSkillWith title filter parser def
