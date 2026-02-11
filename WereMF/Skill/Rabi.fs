module WereMF.Skill.Rabi

open System
open FSharpPlus
open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli

type MilkType =
    | Fresh  // 鲜奶
    | Dry    // 毒奶

type RabiSkill =
    {
        MilkType : MilkType
    }
    interface ISkill

// 解析兔子的输入，格式: "玩家ID x" 或 "玩家ID d"
// x = 鲜奶(可以让目标再次行动)，d = 毒奶(造成死亡)
let parseRabbitInput (input: string) : Result<PlayerId * MilkType, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 when parts[0] = "0" -> Ok (PlayerId 0, Fresh)
    | 2 ->
        let playerIdResult = parsePlayerId parts[0]
        let milkType =
            match parts[1].ToLower() with
            | "x" -> Ok Fresh
            | "d" -> Ok Dry
            | _ -> Error "请输入 x（鲜奶）或 d（毒奶）"
        
        match playerIdResult, milkType with
        | Ok playerId, Ok mt -> Ok (playerId, mt)
        | Error e, _ -> Error e
        | _, Error e -> Error e
    | _ -> Error "请输入格式: 玩家编号 奶类型(x/d)"

let rabbitSendSkill ps game =
    let title = "输入要投喂的玩家编号和奶类型（x=鲜奶，d=毒奶），输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能投喂自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = { MilkType = Fresh } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! playerId, milkType = parseRabbitInput input
        let! playerId = Ok playerId |> filter
        let rabiSkill = Skill.New ps playerId { MilkType = milkType }
        [ rabiSkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
