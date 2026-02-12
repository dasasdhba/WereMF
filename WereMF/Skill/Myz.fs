module WereMF.Skill.Myz

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
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
    interface ISkillCost with
        member this.Cost sending = monad {
            if this.Threaten.Force |> not then this else
            let! context = State.get
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (d: MyzRole) -> { d with Revealed = true})
                             handler
            let context = { context with Game = entity |> context.Game.UpdateEntity }
            let sender = sending |> getSenderName context.Game
            sendMessage { Type = Public; Content = $"{sender}自爆了身份！" }
            sendMessage { Type = Public; Content = $"{entity.Player.Name}是{sender}" }
            sendMessage { Type = Public; Content = $"{sender}今晚的威胁将强制生效！" }
            do! State.put context
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let target = sending |> getRealTarget
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let recv = target |> getPlayerName context.Game
                let night = context.Night.AddMessage $"{sender}想威胁{recv}，被Doge挡了"
                do! State.put { context with Night = night }
                this
            else
            
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            if this.IsNight then
                let pending = context.Night.PendingSkills
                let idx = pending |> List.indexed |> List.filter (fun (i, p) -> p.Source = target)
                let context =
                    if idx.IsEmpty then
                        sendMessage { Type = ToPlayer entity.Player; Content = "失败" }
                        context
                    else
                    let i, ps = idx |> List.randomChoiceWith context.Main.Rng
                    let ps = { ps with Threaten = Some this.Threaten }
                    let pending = pending |> List.updateAt i ps
                    let night = { context.Night with PendingSkills = pending }
                    { context with Night = night }
                do! State.put context
                this
            else
                let tEntity = target |> context.Game.GetEntity
                let threaten = {
                    Type = DayVote (this.Threaten.Target, this.Threaten.Force)
                    Source = this.Threaten.Source
                }
                let tEntity = { tEntity with State.Threaten = Some threaten }
                let context = { context with Game = tEntity |> context.Game.UpdateEntity }
                do! State.put context
                this
    }

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
    
let parseMyzPartInput (input: string) : Result<PlayerId * bool * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    if parts.Length < 2 then
        Error "请至少输入目标编号，威胁类型（n：晚上；d：白天）"
    else        
    monad {
        let! targetId = parsePlayerId parts[0]
        let! isNight =
            match parts[1].ToLower() with
            | "n" -> Ok true
            | "d" -> Ok false
            | _ -> Error "请输入 n（晚上威胁）或 d（白天威胁）"
        let isForce = parts.Length >= 3 && parts[2].ToLower() = "f"
        return (targetId, isNight, isForce)
    }

// myz技能发送
let myzSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let reveal =
        match ps.Handler.GetFromEntity entity with
        | :? MyzRole as myz -> myz.Revealed
        | _ -> true
    let reveal = reveal |> not
    
    let title = "输入要威胁的玩家编号，威胁目标的编号，威胁类型（n：晚上；d：白天），输入 0 放弃"
    let title = if reveal then title else title + "；在结尾输入 f 以自爆身份，并使威胁强制生效"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能威胁自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let targetFilter = giveUpOrFilterWith (filterNonExists game)
    let def () =
        let title = "输入威胁目标的编号，威胁类型（n：晚上；d：白天）"
        let title = if reveal then title else title + "；在结尾输入 f 以自爆身份，并使威胁强制生效"
        let parser input = monad {
            let! targetId, isNight, isForce = parseMyzPartInput input
            if isForce && reveal then
                return! Error "你已经自爆过了"
            else
                
            let! targetId = Ok targetId |> targetFilter
            targetId, isNight, isForce
        }
        let msg = { Type = ToPlayer entity.Player ; Content = title }
        let targetId, isNight, isForce = requestInputWithMessage msg parser
        { Threaten = { Source = ps.Source; Target = targetId; Force = isForce }; IsNight = isNight } :> ISkill
    
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
