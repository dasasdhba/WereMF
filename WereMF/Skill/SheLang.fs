module WereMF.Skill.SheLang

open FSharpPlus
open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.SheLang

type SheLangSkill =
    {
        Target2 : PlayerId option
    }
    interface ISkill

// 解析铯郎的输入，格式: "玩家ID" 或 "玩家ID1 玩家ID2"
let parseSheLangInput (input: string) : Result<PlayerId * PlayerId option, string> = monad {
    let config = {
        MaxCount = 2
        MaxCountError = Some "最多选择 2 个玩家"
        DuplicateError = Some "不能重复选择同一个玩家"
    }
    let! results = parseMultiPlayerId config input
    match results with
    | GiveUp -> PlayerId 0, None
    | Targets ids ->
        match ids.Length with
        | 1 -> ids[0], None
        | v when v >= 2 -> ids[0], Some ids[1]
        | _ -> return! Error "未知格式"
}

let sheLangSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let lastSelected, isDisabled = 
        match ps.Handler.GetFromEntity entity with
        | :? SheLangRole as sl -> sl.LastSelected.Selected, sl.IsDisabled()
        | _ -> [], false
    
    let title = "输入要扔弹簧的玩家编号（最多 2 个），输入 0 放弃"
    
    let filter = filterNonExists game
                    >> filterDead game
                    >> filterExceptIndexList lastSelected "不能连续对同一个玩家使用弹簧"
                    >> filterSelectable game
                    >> filterKidnapped ps
                    >> (if isDisabled then filterDisabled "你上晚发了两个弹簧，今晚无法行动" else id)
    let filter = giveUpOrFilterWith filter
    let def () = { Target2 = None } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! results = parseSheLangInput input
        match results with
        | PlayerId v, _ when v <= 0 ->
            [ None ]
        | target1, target2 ->
            let! target1 = Ok target1 |> filter
            let target2 = target2 |> Option.map (fun t -> Ok t |> filter)
            match target2 with
            | None ->
                let skill = Skill.New ps target1 { Target2 = None }
                [ skill |> Some ]
            | Some r ->
                let! t2 = r
                let t2 = if t2 <= PlayerId 0 then None else Some t2
                let skill = Skill.New ps target1 { Target2 = t2 }
                [ skill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
