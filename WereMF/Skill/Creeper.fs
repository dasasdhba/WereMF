module WereMF.Skill.Creeper

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Entity
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.Module.Api
open WereMF.State
open WereMF.Role.Creeper

type CreeperSkill =
    | CreeperSkill
    interface ISkill
    interface ISkillCost with
        member this.Cost sending = monad {
            let! (main, game), night = State.get

            let source = sending |> getSource
            let entity = source |> game.GetEntity
            let handler = sending |> getHandler
            let target = sending |> getRealTarget
            let entity = entity |> updateRoleWithHandler
                             (fun (c: CreeperRole) -> { c with
                                                          BombCount = c.BombCount - 1
                                                          PlacedList = target :: c.PlacedList })
                             handler
            let game = game.UpdateEntity entity
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
                sendRawMessage { Type = ToPlayer (sending |> getSource |> game.GetEntity).Player ; Content = "失败" } ApiType.CreeperSkillFailByDogeNotify
                let night = night.AddMessage $"{sender}想给{recv}埋炸弹，被Doge挡了"
                do! State.put ((main, game), night)
                this
            else
                let tEntity = target |> game.GetEntity
                let tEntity = { tEntity with State.QueuedBomb = tEntity.State.QueuedBomb + 1 }
                let game = game.UpdateEntity tEntity
                do! State.put ((main, game), night)
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
                >> filterSelectable ps.Source game
                >> filterKidnapped ps
                >> filterExceptIndexList placedList "该玩家已被埋过炸药"
                >> (if bombCount <= 0 then filterDisabled "你没有炸药了" else id)
    let filter = giveUpOrFilterWith filter
    let def () = CreeperSkill :> ISkill
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r CreeperSkill |> Some ])
    
    ps |> sendSkillWith title ApiType.RequestCreeperSkill filter parser def
