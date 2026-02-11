module WereMF.Skill.Creeper

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.Creeper

type CreeperSkill =
    | CreeperSkill
    interface ISkill

// 爬行者技能发送
let creeperSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let bombCount, placedList = 
        match ps.Handler.GetFromEntity entity with
        | :? CreeperRole as creeper -> (creeper.BombCount, creeper.PlacedList)
        | _ -> (0, [])
    
    let title = $"输入要在谁身上埋炸药（剩余 {bombCount} 个炸弹），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList placedList "该玩家已被埋过炸药"
                >> (if bombCount <= 0 then filterDisabled "你没有炸药了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = CreeperSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r CreeperSkill |> Some ])
    
    ps |> sendSkillWith title filter parser def
