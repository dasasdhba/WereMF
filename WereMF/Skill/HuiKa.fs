module WereMF.Skill.HuiKa

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Game
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.HuiKa

type HuiKaSkill =
    | HuiKaSkill
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let target = sending |> getRealTarget
            let recv = target |> getPlayerName context.Game
            if target |> isDoged context.Night then
                let sender = sending |> getSenderName context.Game
                let night = context.Night.AddMessage $"{sender}想给{recv}丢烟雾弹，被Doge挡了"
                do! State.put { context with Night = night }
                this
            else
                let tEntity = target |> context.Game.GetEntity
                sendMessage { Type = Public ; Content = $"{recv}被烟雾弥漫！" }
                let tEntity = { tEntity with State = tEntity.State |> EntityState.addSmog }
                let context = { context with Game = context.Game.UpdateEntity tEntity }
                let context =
                    if tEntity.State.SmogCount < 2 then context else
                    sendMessage { Type = Public ; Content = $"{recv}窒息了！" }
                    let tEntity = { tEntity with State.Smog = [] }
                    let context = { context with Game = context.Game.UpdateEntity tEntity }
                    let r = RoleContext.Create context.Main context.Game
                    let request = DeadRequest.New Kill
                    let r, tEntity = tEntity |> Entity.requestDead request r
                    let r = involveIfDoge tEntity context.Night r
                    let main, game = r.Get ()
                    { context with Main = main ; Game = game }
                
                do! State.put context
                sendMessage { Type = Public ; Content = $"\n{printNightSummary context.Game.Entities}" }
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
                >> filterSelectableWithoutSmog game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = HuiKaSkill :> ISkill
    
    let createSkill id = Skill.New ps id HuiKaSkill
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser def
