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
        Success : PlayerNightState option
    }
    interface ISkill
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                let night = night.AddMessage $"{sender}想给{recv}喂奶，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else

            let source = sending |> getSource
            let handler = sending |> getHandler
            let entity = source |> game.GetEntity
            let round = entity |> getFromRoleWithHandler
                                (fun (r: RabiRole) -> r.Round)
                                handler

            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
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
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)
                if this.MilkType = Fresh then
                    let handlers = getValidHandlers tEntity.Role
                    let handler = handlers |> List.randomChoiceWith main.Rng
                    let chara = getHandlerCharaType handler tEntity
                    if chara = Rabi then this else
                    let ps = createPendingSkill handler tEntity
                    let night = { night with PendingSkills = ps :: night.PendingSkills }
                    do! State.put ((main, game), night)
                    this
                else
                    let state = night.GetPlayerState target
                    let state = { state with Blocked = true }
                    { this with Success = Some state }
            else

            this
    }
    interface ISkillExecuteQueued with
        member this.Execute sending = monad {
            if this.Success.IsNone then this else
            let state = this.Success.Value
            let! (main, game), night = State.get
            let night = night.SetPlayerState state
            do! State.put ((main, game), night)
            this
        }
    interface ISkillSummary with
         member this.Priority = 2
         member this.GetRealTarget sending =
             sending |> getRealTarget
         member this.Summarize sending = monad {
            if this.Success.IsNone then None else

            let! (main, game), night = State.get

            let recv = sending.Target |> getPlayerName game
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            sendMessage { Type = Public; Content = $"{recv}被喂了毒奶！" }
            Some {
                Target = tEntity
                Request = DeadRequest.New Kill
            }
    }

// 解析兔子的输入，格式: "玩家ID x" 或 "玩家ID d"
// x = 鲜奶(可以让目标再次行动)，d = 毒奶(造成死亡)
let parseRabbitInput (input: string) : Result<PlayerId * MilkType, string> = monad {
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 1 when parts[0] = "0" -> PlayerId 0, Fresh
    | 2 ->
        let! playerId = parsePlayerId parts[0]
        let! milkType =
            match parts[1].ToLower() with
            | "x" -> Ok Fresh
            | "d" -> Ok Dry
            | _ -> Error "请输入 x（鲜奶）或 d（毒奶）"
        
        playerId, milkType
    | _ -> return! Error "请输入格式: 玩家编号 奶类型(x/d)"
}

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
        let t = if yes then Fresh else Dry
        { MilkType = t; Success = None } :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! playerId, milkType = parseRabbitInput input
        let! playerId = Ok playerId |> filter
        if playerId <= PlayerId 0 then [ None ] else
        let rabiSkill = Skill.New ps playerId { MilkType = milkType ; Success = None }
        [ rabiSkill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
