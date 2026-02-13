module WereMF.Skill.SheLang

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.SheLang

type SheLangSkill =
    {
        Disabled : bool
        Success : PlayerNightState option
    }
    static member New() = { Disabled = false ; Success = None }
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get
            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (s: SheLangRole) -> { s with
                                                         LastSelected = s.LastSelected.Add target
                                                         Disabled = if this.Disabled then Some true else s.Disabled })
                             handler
            let game = entity |> game.UpdateEntity
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
                let night = night.AddMessage $"{sender}想给{recv}扔弹簧，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                let state = night.GetPlayerState target
                let state = { state with Spring = true }
                { this with Success = Some state }
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
    let def () = SheLangSkill.New () :> ISkill
    
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
                let skill = Skill.New ps target1 (SheLangSkill.New())
                [ skill |> Some ]
            | Some r ->
                let! t2 = r
                let s = [ Skill.New ps target1 (SheLangSkill.New()) |> Some ]
                let s = 
                    if t2 <= PlayerId 0 then s
                    else s @ [ Skill.New ps t2 { Disabled = true ; Success = None } |> Some ]
                s
    }
    
    ps |> sendSkillWith title filter parser def
