module WereMF.Skill.CTF

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Role.XianSong
open WereMF.State
open WereMF.Role.CTF

let private updateStateIfBug (night: NightContext) entity =
    // 闲松技能失效
    let hs = entity.Role |> getValidHandlers
             |> List.filter (fun h -> (entity |> getHandlerCharaType h) = XianSong)
    let mutable e = entity
    for h in hs do
        e <- e |> updateRoleWithHandler
                             (fun (x: XianSongRole) -> { x with Disabled = Some true })
                             h
    let entity = e
    
    // 暴毙记录与阻断
    if entity.State.BugCount < 3 then night, entity else
    let state = night.GetPlayerState entity.Player.Id
    let state = { state with Blocked = true }
    let night = night.SetPlayerState state
    let night =
        if night.BugPlayers |> List.contains entity.Player.Id then
            night
        else
            { night with BugPlayers = night.BugPlayers @ [ entity.Player.Id ] }
    night, entity
    
let updateBugOnNight (night: NightContext) entity =
    if entity.State.Bug = None then night, entity else
    let night = night.AddMessage $"{entity.Player.Name}身上多了一只虫子"
    let entity = { entity with State.Bug = entity.State.Bug |> Option.map (fun b -> b + 1) }
    updateStateIfBug night entity
    
let updateSpringBugOnNight (night: NightContext) entity =
    if entity.State.Bug = None then night, entity else
    let night = night.AddMessage $"{entity.Player.Name}身上多了无数只虫子"
    let entity = { entity with State.Bug = entity.State.Bug |> Option.map (fun b -> b + 3) }
    updateStateIfBug night entity

let private addBugSilent entity=
    let bug = entity.State.Bug
    let bug =
        match bug with
        | None -> Some 0
        | Some b -> Some (b + 1)
    { entity with State.Bug = bug }

let private addBugWithMsg (night: NightContext)  entity=
    let bug = entity.State.Bug
    let bug, night =
        match bug with
        | None -> Some 0, night
        | Some b ->
            let msg = $"{entity.Player.Name}身上多了一只虫子"
            Some (b + 1), night.AddMessage msg
    night, { entity with State.Bug = bug }

type CTFSkill =
    | CTFSkill
    interface ISkill
    interface ISkillCanExecute with
        member this.CanExecute context sending =
            let (main, game), night = context 
            canExecuteIfAlive game sending
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get

            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let entity = entity |> updateRoleWithHandler
                             (fun (c: CTFRole) -> { c with BugCount = c.BugCount - 1 })
                             handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            this
        }
    interface ISkillExecute with
        member this.Execute sending = monad {
            let! (main, game), night = State.get

            let source = sending |> getSource
            let entity = source |> game.GetEntity
            if sending.Spring.IsSome && sending.Spring.Value = Recursed then
                let target = sending.Target
                let tEntity = target |> game.GetEntity

                let entity = addBugSilent entity
                let tEntity = addBugSilent tEntity
                let night, entity = updateSpringBugOnNight night entity
                let night, tEntity = updateSpringBugOnNight night tEntity

                let game = game.UpdateEntity entity
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)
                this
            else

            let target = sending |> getRealTarget
            if target |> isDoged night then
                let sender = sending |> getSenderName game
                let recv = target |> getPlayerName game
                let night = night.AddMessage $"{sender}想给{recv}丢虫子，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                let tEntity = target |> game.GetEntity
                let night, tEntity = addBugWithMsg night tEntity
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)
                this
        }

// CTF技能发送
let ctfSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let bugCount =
        match ps.Handler.GetFromEntity entity with
        | :? CTFRole as ctf -> ctf.BugCount
        | _ -> 0
    
    let title = $"输入要释放虫子的玩家编号（剩余 {bugCount} 只虫子），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能给自己虫子"
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if bugCount <= 0 then filterDisabled "你没有虫子了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = CTFSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r CTFSkill |> Some ])
    
    ps |> sendSkillWith title filter parser def
