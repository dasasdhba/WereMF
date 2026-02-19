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
        Success : PlayerNightState option
    }
    static member New() = { Success = None }
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
                                                         LastSelected = s.LastSelected.Add target })
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

let sheLangSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let last =
        match ps.Handler.GetFromEntity entity with
        | :? SheLangRole as se -> se.LastSelected.Selected
        | _ -> []
        
    let title = "输入要扔弹簧的玩家编号，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList last "不能连续对同一个玩家使用弹簧"
    let filter = giveUpOrFilterWith filter
    let def () = (SheLangSkill.New ()) :> ISkill
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ Skill.New ps r (SheLangSkill.New ()) |> Some ])
    ps |> sendSkillWith title filter parser def