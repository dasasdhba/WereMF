module WereMF.Role.Creeper

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type CreeperRole =
    {
        BombCount : int
        PlacedList : PlayerId list
    }
    static member New () = { BombCount = 3 ; PlacedList = [] }
    interface IRole with
        member this.Base = {
            CharaType = Creeper
            Priority = 0
            SummaryName = Creeper.ToString ()
        }

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
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    
    ps |> sendSkillWith title filter parser
