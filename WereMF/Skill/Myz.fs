module WereMF.Skill.Myz

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Role.Myz

type MyzSkill =
    {
        Threaten : ThreatenSkill
    }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            if this.Threaten.Force |> not then this else
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (d: MyzRole) -> { d with Revealed = true})
                             handler
            let game = entity |> game.UpdateEntity
            let sender = sending |> getSenderName game
            sendRawMessage { Type = Public; Content = $"{sender}自爆了身份！" } ApiType.MyzSelfRevealBroadcast
            sendRawMessage { Type = Public; Content = $"{source |> getPlayerNameAnonymous game}是{sender}" } ApiType.MyzSelfRevealBroadcast
            sendRawMessage { Type = Public; Content = $"{sender}今晚的威胁将强制生效！" } ApiType.MyzSelfRevealBroadcast
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想威胁{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else

            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let tEntity = target |> game.GetEntity
            if tEntity.State.Threaten |> Option.isSome then
                sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.MyzThreatFailedByAlreadyNotify
                this
            else
            
            let tEntity = { tEntity with State.Threaten = Some false }
            let game = game.UpdateEntity tEntity
            let pending = night.PendingSkills
            let idx = pending |> List.indexed |> List.filter (fun (i, p) -> p.Source = target && p.Threaten = None)
            let night =
                if idx.IsEmpty then
                    sendRawMessage { Type = ToPlayer entity.Player; Content = "失败" } ApiType.MyzThreatFailedByNoSkillNotify
                    night
                else
                    let i, ps = idx |> List.randomChoiceWith main.Rng
                    let ps = { ps with Threaten = Some this.Threaten }
                    let pending = pending |> List.updateAt i ps
                    let night = { night with PendingSkills = pending }
                    night
            do! State.put ((main, game), night)
            this
    }

// 解析myz输入，格式: "玩家ID 威胁目标ID n/d [f]"
// n=晚上威胁，d=白天威胁，f=强制
let parseMyzInput (input: string) : Result<PlayerId * PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    if parts.Length < 2 then
        Error "请至少输入玩家编号，目标编号"
    else        
    monad {
        let! playerId = parsePlayerId parts[0]
        let! targetId = parsePlayerId parts[1]
        let isForce = parts.Length >= 3 && parts[2].ToLower() = "f"
        return (playerId, targetId, isForce)
    }
    
let parseMyzPartInput (input: string) : Result<PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    if parts.Length < 1 then
        Error "请至少输入目标编号"
    else        
    monad {
        let! targetId = parsePlayerId parts[0]
        let isForce = parts.Length >= 2 && parts[1].ToLower() = "f"
        return (targetId, isForce)
    }

// myz技能发送
let myzSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let reveal =
        match ps.Handler.GetFromEntity entity with
        | :? MyzRole as myz -> myz.Revealed
        | _ -> true
    let reveal = reveal
    
    let title = "输入要威胁的玩家编号，威胁目标的编号，输入 0 放弃"
    let title = if reveal then title else title + "；在结尾输入 f 以自爆身份，并使威胁强制生效"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能威胁自己"
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let targetFilter = giveUpOrFilterWith (filterNonExists game)
    let def () =
        let title = "输入威胁目标的编号"
        let title = if reveal then title else title + "；在结尾输入 f 以自爆身份，并使威胁强制生效"
        let parser input = monad {
            let! targetId, isForce = parseMyzPartInput input
            if isForce && reveal then
                return! Error "你已经自爆过了"
            else
                
            let! targetId = Ok targetId |> targetFilter
            targetId, isForce
        }
        let msg = { Type = ToPlayer entity.Player ; Content = title }
        let targetId, isForce = requestInputWithRawMessage msg ApiType.RequestMyzSkillForceThreaten parser
        { Threaten = { Source = ps.Source; Target = targetId; Force = isForce } } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> =
        let trimmed = input.Trim()
        if trimmed = "0" then
            Ok [ None ]
        else monad {
            let! playerId, targetId, isForce = parseMyzInput input
            if isForce && reveal then
                return! Error "你已经自爆过了"
            else
            
            let! playerId = Ok playerId |> filter
            let! targetId = Ok targetId |> targetFilter
            let skill = Skill.New ps playerId { Threaten = { Source = ps.Source; Target = targetId; Force = isForce } }
            [ skill |> Some ]
        }
        
    ps |> sendSkillWith title ApiType.RequestMyzSkill filter parser def
