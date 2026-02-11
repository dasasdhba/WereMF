module WereMF.Skill.ShiWu

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.ShiWu

type ShiWuSkill =
    | ShiWuSkill
    interface ISkill

// 获取上一晚绑架过的玩家列表
let getShiWuLastSelected (handler: RoleHandler) (entity: Entity) : PlayerId list =
    match handler.GetFromEntity entity with
    | :? ShiWuRole as shiWu ->
        match shiWu.LastSelected with
        | SelectionState record -> record.LastNight
    | _ -> []

// 实物技能发送
let shiWuSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let lastSelected = getShiWuLastSelected ps.Handler entity
    
    let title = "输入一名玩家的编号进行绑架，输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "不能绑架自己"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList lastSelected "不能连续绑架同一个玩家"
    let filter = giveUpOrFilterWith filter
    let def () = ShiWuSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r ShiWuSkill |> Some ])
    
    ps |> sendSkillWith title filter parser def
