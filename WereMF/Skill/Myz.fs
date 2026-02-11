module WereMF.Skill.Myz

open System
open FSharpPlus
open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.Myz

type MyzSkill =
    {
        Threaten : ThreatenSkill
        IsNight : bool
    }
    interface ISkill

// 解析myz输入，格式: "玩家ID 威胁目标ID n/d [f]"
// n=晚上威胁，d=白天威胁，f=强制
let parseMyzInput (input: string) : Result<PlayerId * PlayerId * bool * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    if parts.Length < 3 then
        Error "请至少输入玩家编号，目标编号，威胁类型（n：晚上；d：白天）"
    else        
    monad {
        let! playerId = parsePlayerId parts[0]
        let! targetId = parsePlayerId parts[1]
        let! isNight =
            match parts[2].ToLower() with
            | "n" -> Ok true
            | "d" -> Ok false
            | _ -> Error "请输入 n（晚上威胁）或 d（白天威胁）"
        let isForce = parts.Length >= 4 && parts[3].ToLower() = "f"
        return (playerId, targetId, isNight, isForce)
    }

// myz技能发送
let myzSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let reveal =
        match ps.Handler.GetFromEntity entity with
        | :? MyzRole as myz -> myz.Revealed
        | _ -> false
    
    let title = "输入要威胁的玩家编号，威胁目标的编号，威胁类型（n：晚上；d：白天），输入 0 放弃"
    let title = if reveal then title else title + "；在结尾输入 f 以自爆身份，并使威胁强制生效"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能威胁自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let targetFilter = giveUpOrFilterWith (filterNonExists game)
    let def () = { Threaten = { Source = ps.Source; Target = PlayerId 0; Force = false }; IsNight = true } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> =
        let trimmed = input.Trim()
        if trimmed = "0" then
            Ok [ None ]
        else monad {
            let! playerId, targetId, isNight, isForce = parseMyzInput input
            if isForce && reveal then
                return! Error "你已经自爆过了"
            else
            
            let! playerId = Ok playerId |> filter
            let! targetId = Ok targetId |> targetFilter
            let skill = Skill.New ps playerId { Threaten = { Source = ps.Source; Target = targetId; Force = isForce }; IsNight = isNight }
            [ skill |> Some ]
        }
        
    ps |> sendSkillWith title filter parser def
