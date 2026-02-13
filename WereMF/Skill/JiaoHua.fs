module WereMF.Skill.JiaoHua

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Entity
open WereMF.Module.Skill
open WereMF.Role.ShiWu

type JiaoHuaSkill =
    | JiaoHuaSkill
    interface ISkill
    interface ISkillExecute with
        member this.Execute (sending: SendingSkill) = monad {
            let! (main, game), night = State.get
            let target = sending |> getRealTarget
            let entity = game.GetEntity target
            let handler = entity |> getQueriedHandler main.Rng
            let player = (sending |> getSource |> game.GetEntity).Player
            
            // 烟雾
            if handler.IsNone then
                sendMessage { Type = ToPlayer player; Content = "失败" }
                this
            else
            
            // 实物
            let handler = handler.Value
            let entity = entity |> exposeIfShiWu handler
            let game = game.UpdateEntity entity
            do! State.put ((main, game), night)
            
            let name = entity |> getQueriedName handler
            sendMessage { Type = ToPlayer player; Content = name }
            this
        }

let jiaoHuaSendSkill ps game =
    let title = "输入一名玩家的编号查询其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能查自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let def () = JiaoHuaSkill :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ (Skill.New ps r JiaoHuaSkill) |> Some ])
    ps |> sendSkillWith title filter parser def

