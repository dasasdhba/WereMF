module WereMF.Skill.FenXia

open FSharpPlus
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.FenXia

type FenXiaSkill =
    | FenXiaSkill
    interface ISkill

// 过滤：允许选择任何玩家，但根据生死状态决定粉条消耗
let filterFenXia (game: GameContext) (fenCount: int) = function
    | Ok playerId ->
        let entity = game.GetEntity playerId
        let isDead = entity.State |> EntityState.isDead
        let cost = if isDead then 2 else 1
        
        if cost > fenCount then
            if isDead then
                Error "你的粉条不足（需要 2 根）"
            else
                Error "你的粉条不足（需要 1 根）"
        else
            Ok playerId
    | value -> value

// 粉侠技能发送
let fenXiaSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let fenCount = 
        match ps.Handler.GetFromEntity entity with
        | :? FenXiaRole as fenXia -> fenXia.FenCount
        | _ -> 0
    
    let title = $"输入要获取技能的角色编号（剩余 {fenCount} 根粉条），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterExceptIndex ps.Source "你不能给自己粉条"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterFenXia game fenCount
                >> (if fenCount <= 0 then filterDisabled "你没有粉条了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = FenXiaSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r FenXiaSkill |> Some ])
    
    ps |> sendSkillWith title filter parser def
