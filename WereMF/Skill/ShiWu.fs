module WereMF.Skill.ShiWu

open System
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.ShiWu

type ShiWuSkill =
    {
        Success : bool
        Broadcast : bool
    }
    static member New () = { Success = false ; Broadcast = false }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (d: ShiWuRole) -> { d with
                                                          LastSelected = d.LastSelected.Add target
                                                          Broadcasted = if this.Broadcast then true else d.Broadcasted })
                             handler
            let game = entity |> game.UpdateEntity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            let sender = sending |> getSenderName game
            let recv = target |> getPlayerName game
            if target |> isDoged night then
                let night = night.AddMessage $"{sender}想绑架{recv}，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else

            let source = sending |> getSource
            let tEntity = target |> game.GetEntity
            sendMessage { Type = Public ; Content = $"{recv}被{sender}绑架了！" }
            let game =
                if this.Broadcast |> not then game else
                let h = tEntity |> Entity.getQueriedHandler main.Rng

                if h.IsNone then
                    let entity = source |> game.GetEntity
                    sendMessage { Type = ToPlayer entity.Player; Content = "播报失败" }
                    game
                else
                    let h = h.Value
                    let tEntity = tEntity |> exposeIfShiWu h
                    let game = game.UpdateEntity tEntity
                    let name = tEntity |> Entity.getHandlerName h
                    sendMessage { Type = Public; Content = $"{sender}公开了{recv}的身份！" }
                    sendMessage { Type = Public; Content = $"{recv}是{name}" }
                    game

            let tEntity = { tEntity with State.Kidnapped = source :: tEntity.State.Kidnapped }
            let game = tEntity |> game.UpdateEntity
            let pending = night.PendingSkills
            let idx = pending |> List.indexed |> List.filter (fun (i, p) -> p.Source = target)
            let night =
                if idx.IsEmpty then night else
                let i, ps = idx |> List.randomChoiceWith main.Rng
                let ps = { ps with Kidnapped = true }
                let pending = pending |> List.updateAt i ps
                let night = { night with PendingSkills = pending }
                night
            do! State.put ((main, game), night)
            { this with Success = true }
        }
    interface ISkillSummary with
        member this.Priority = 1
        member this.GetRealTarget sending =
            sending |> getRealTarget
        member this.Summarize sending = monad {
            if this.Success |> not then None else

            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler

            let exposed = entity |> getFromRoleWithHandler
                            (fun m -> m.Exposed)
                            handler
            if exposed |> not then None else

            let sender = sending |> getSenderName game
            sendMessage { Type = Public ; Content = $"{sender}被查出了身份，{sender}撕票了！" }
            let target = sending |> getRealTarget
            let tEntity = target |> game.GetEntity
            Some {
                Target = tEntity
                Request = DeadRequest.New Kill
            }
        }
        
let parseShiWuInput (input: string) : Result<PlayerId * bool, string> =
    let parts = input.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    match parts.Length with
    | 0 -> Error "输入不能为空"
    | 1 ->
        parsePlayerId parts[0] |> Result.map (fun id -> (id, false))
    | 2 ->
        let broadcast = parts[1].ToLower() = "b"
        if not broadcast then
            Error "无效输入格式，请使用: 玩家ID [b]"
        else
            parsePlayerId parts[0] |> Result.map (fun id -> (id, true))
    | _ ->
        Error "无效输入格式，请使用: 玩家ID [b]"

// 实物技能发送
let shiWuSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let handler = ps.Handler
    let lastSelected, broadcasted =
        match handler.GetFromEntity entity with
        | :? ShiWuRole as shiWu -> shiWu.LastSelected.Selected, shiWu.Broadcasted
        | _ -> [], false
    
    let title = "输入一名玩家的编号进行绑架，输入 0 放弃"
    let title = if broadcasted then title
                else title + "；在结尾输入 b 表示公开被绑架者的身份"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能绑架自己"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList lastSelected "你不能连续绑架同一个玩家"
    let filter = giveUpOrFilterWith filter
    let def () = (ShiWuSkill.New ()) :> ISkill
    
    let parser (input: string) : Result<Skill option list, string> = monad {
        let! target, broadcast = parseShiWuInput input
        if broadcasted && broadcast then return! Error "你已经使用过播报技能" else
        let! target = Ok target |> filter
        if target <= PlayerId 0 then [ None ] else
        let skill = Skill.New ps target { Success = false; Broadcast = broadcast }
        [ skill |> Some ]
    }
    
    ps |> sendSkillWith title filter parser def
