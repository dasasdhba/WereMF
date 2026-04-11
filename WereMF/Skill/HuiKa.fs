module WereMF.Skill.HuiKa

open System.Text.Json.Nodes
open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Entity
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Role.HuiKa

type HuiKaSkill =
    {
        Success : PlayerId option
    }
    static member New () = { Success = None }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                sendRawMessage { Type = ToPlayer (sending |> getSource |> game.GetEntity).Player ; Content = "失败" } ApiType.HuikaSkillFailByDogeNotify
                let night = night.AddMessage $"{sender}想给{recv}丢烟雾弹，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                { this with Success = Some target }
        }
    interface ISkillExecuteQueued with
        member this.Execute sending = monad {
            if this.Success.IsNone then this else

            let! (main, game), night = State.get
            let target = this.Success.Value
            let recv = target |> getPlayerNameAnonymous game
            let tEntity = target |> game.GetEntity
            sendRawMessage { Type = Public ; Content = $"{recv}被烟雾弥漫！" } ApiType.HuikaSmogBroadcast
            let tEntity = { tEntity with State = tEntity.State |> EntityState.addSmog }
            let game = game.UpdateEntity tEntity
            let main, game, night =
                if tEntity.State.SmogCount < 2 then main, game, night else
                sendRawMessage { Type = Public ; Content = $"{recv}窒息了！" } ApiType.HuikaSmogKillBroadcast
                let dead = tEntity, (main, game)
                let request = DeadRequest.New Kill
                let dead = dead |> Entity.requestDead request
                let tEntity, (main, game) = dead
                let (main, game), night = involveIfDoge tEntity ((main, game), night)
                let night = blockIfLeaf tEntity night
                main, game, night

            do! State.put ((main, game), night)
            sendMessage {
                Type = Public
                Content = $"\n{printNightSummary game.Entities}"
                Api = ApiType.GameUpdateNight
                Data = game.ToJsonValue ()
            }
            this
        }

// 获取最大投掷数量（第一轮2个，之后1个）
let getHuiKaMaxCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? HuiKaRole as huiKa -> if huiKa.FirstRound then 1 else 2
    | _ -> 1

// 灰卡比技能发送
let huiKaSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let maxCount = getHuiKaMaxCount ps.Handler entity
    
    let title = $"输入要投掷烟雾弹的玩家编号（最多 {maxCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = maxCount
        MaxCountError = Some $"最多投掷 {maxCount} 个烟雾弹"
        DuplicateError = Some "不能重复投掷同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectableWithoutSmog ps.Source game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = (HuiKaSkill.New ()) :> ISkill
    
    let createSkill id = Skill.New ps id (HuiKaSkill.New ())
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title ApiType.RequestHuikaSkill filter parser def
