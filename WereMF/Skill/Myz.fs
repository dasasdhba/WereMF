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
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (d: MyzRole) -> { d with Revealed = true})
                             handler
            let game = entity |> game.UpdateEntity
            let sender = sending |> getSenderName game
            sendMessage { Type = Public; Content = $"{sender}自爆了身份！" }
            sendMessage { Type = Public; Content = $"{entity.Player.Name}是{sender}" }
            sendMessage { Type = Public; Content = $"{sender}今晚的威胁将强制生效！" }
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
            if this.IsNight then
                let pending = night.PendingSkills
                let idx = pending |> List.indexed |> List.filter (fun (i, p) -> p.Source = target && p.Threaten = None)
                let night =
                    if idx.IsEmpty then
                        sendMessage { Type = ToPlayer entity.Player; Content = "失败" }
                        night
                    else
                        let i, ps = idx |> List.randomChoiceWith main.Rng
                        let ps = { ps with Threaten = Some this.Threaten }
                        let pending = pending |> List.updateAt i ps
                        let night = { night with PendingSkills = pending }
                        night
                do! State.put ((main, game), night)
                this
            else
                let tEntity = target |> game.GetEntity
                if tEntity.State.Threaten.IsSome then
                    sendMessage { Type = ToPlayer entity.Player; Content = "失败" }
                    this
                else
                let tg = this.Threaten.Target
                let tgPlayer = (tg |> game.GetEntity).Player
                let force = this.Threaten.Force
                let msf = if force then "你被强制威胁" else "你被威胁"
                let msb = if tg <= PlayerId 0 then "弃票"
                          else $"把票投给{tgPlayer.ToInGameString()}"
                sendMessage { Type = ToPlayer tEntity.Player ; Content = msf + msb }
                
                let threaten = {
                    Type = DayVote (this.Threaten.Target, this.Threaten.Force)
                    Source = this.Threaten.Source
                }
                let tEntity = { tEntity with State.Threaten = Some threaten }
                let game = tEntity |> game.UpdateEntity
                do! State.put ((main, game), night)
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
    let reveal = reveal
    
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
