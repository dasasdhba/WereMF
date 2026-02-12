module WereMF.Skill.Rabi

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.Rabi
open WereMF.State

type MilkType =
    | Fresh  // 鲜奶
    | Dry    // 毒奶

type RabiSkill =
    {
        MilkType : MilkType
    }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! context = State.get
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName context.Game
            let recv = target |> getPlayerName context.Game
            if target |> isDoged context.Night then
                let night = context.Night.AddMessage $"{sender}想给{recv}喂奶，被 doge 挡了"
                do! State.put { context with Night = night }
                this
            else
            
            let source = sending |> getSource
            let handler = sending |> getHandler
            let entity = context.Game.GetEntity source
            let round = entity |> getFromRoleWithHandler
                                (fun (r: RabiRole) -> r.Round)
                                handler
            let round = defaultArg round 0
            
            let target = sending |> getRealTarget
            let tEntity = context.Game.GetEntity target
            let lastMilk = tEntity.State.Milk.HasLastMilk
            
            let force = round >= 2 || lastMilk
            let milkName = match this.MilkType with
                            | Fresh -> "鲜奶"
                            | Dry -> "毒奶"
            let drink =
                if force then
                    sendMessage { Type = ToPlayer tEntity.Player ; Content = $"你被喂{milkName}" }
                    true
                else
                let msg = { Type = ToPlayer tEntity.Player
                            Content = "你被喂奶，要喝吗？（1：喝；0：不喝）" }
                let yes = requestInputWithMessage msg parseBool
                if yes then sendMessage { Type = ToPlayer tEntity.Player; Content = milkName }
                yes
                
            if drink then
                let tEntity = { tEntity with State.Milk = tEntity.State.Milk.Set () }
                let context = { context with Game = context.Game.UpdateEntity tEntity }
                do! State.put context
                if this.MilkType = Fresh then
                    let handlers = getValidHandlers tEntity.Role
                    let handler = handlers |> List.randomChoiceWith context.Main.Rng
                    let chara = getHandlerCharaType handler tEntity
                    if chara = Rabi then this else
                    let ps = createPendingSkill handler tEntity
                    let context = { context with Night.PendingSkills = ps :: context.Night.PendingSkills }
                    do! State.put context
                    this
                else
                    let state = context.Night.GetPlayerState target
                    let state = { state with Blocked = true }
                    let night = context.Night.SetPlayerState state
                    do! State.put { context with Night = night }
                    this
            else
            
            this
    }
    interface ISkillSummary with
         member this.Priority = 1
         member this.GetRealTarget sending =
             sending |> getRealTarget
         member this.Summarize sending = monad {
            if this.MilkType = Fresh then None else

            let! context = State.get

            let recv = sending.Target |> getPlayerName context.Game
            let target = sending |> getRealTarget
            let tEntity = target |> context.Game.GetEntity
            sendMessage { Type = Public; Content = $"{recv}被喂了毒奶！" }
            Some {
                Target = tEntity
                Request = DeadRequest.New Sudden
            }
    }

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

let rabbitSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    
    let title = "输入要投喂的玩家编号和奶类型（x=鲜奶，d=毒奶），输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能投喂自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () =
        let msg = { Type = ToPlayer entity.Player ; Content = "你可以选择给鲜奶还是毒奶（1：鲜奶；0：毒奶）" }
        let yes = requestInputWithMessage msg parseBool
        { MilkType = if yes then Fresh else Dry } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! playerId, milkType = parseRabbitInput input
        let! playerId = Ok playerId |> filter
        let rabiSkill = Skill.New ps playerId { MilkType = milkType }
        [ rabiSkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
