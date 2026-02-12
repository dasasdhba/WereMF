module WereMF.Skill.Creeper

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State
open WereMF.Role.Creeper

type CreeperSkill =
    | CreeperSkill
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! context = State.get
            
            let source = sending |> getSource
            let entity = source |> context.Game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (c: CreeperRole) -> { c with
                                                         BombCount = c.BombCount - 1
                                                         PlacedList = target :: c.PlacedList })
                             handler
            let context = { context with Game = context.Game.UpdateEntity entity }
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
                let night = context.Night.AddMessage $"{sender}想给{recv}埋炸弹，被doge挡了"
                do! State.put { context with Night = night }
                this
            else
                let tEntity = target |> context.Game.GetEntity
                let tEntity = { tEntity with State.Bomb = tEntity.State.Bomb + 1 }
                let context = { context with Game = context.Game.UpdateEntity tEntity }
                do! State.put context
                this
        }

// 爬行者技能发送
let creeperSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let bombCount, placedList = 
        match ps.Handler.GetFromEntity entity with
        | :? CreeperRole as creeper -> (creeper.BombCount, creeper.PlacedList)
        | _ -> (0, [])
    
    let title = $"输入要在谁身上埋炸药（剩余 {bombCount} 个炸弹），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList placedList "该玩家已被埋过炸药"
                >> (if bombCount <= 0 then filterDisabled "你没有炸药了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = CreeperSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r CreeperSkill |> Some ])
    
    ps |> sendSkillWith title filter parser def
