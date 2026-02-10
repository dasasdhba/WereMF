module WereMF.Role.YinMo

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type YinMoRole =
    {
        DiscCount : int
        Disabled : bool option
    }
    static member New count = { DiscCount = count ; Disabled = None }
    member this.IsDisabled ()
        = this.Disabled.IsSome
    interface IRole with
        member this.Base = {
            CharaType = YinMo
            Priority = 2
            SummaryName = YinMo.ToString ()
        }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            let disabled = match this.Disabled with
                            | Some true -> Some false
                            | _ -> None
            { this with Disabled = disabled }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with Disabled = None }

// 音魔技能发送
let yinMoSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let disc, isDisabled = 
        match ps.Handler.GetFromEntity entity with
        | :? YinMoRole as yinMo -> yinMo.DiscCount, yinMo.IsDisabled()
        | _ -> 0, false
    
    let title = $"输入要发唱片的玩家编号（剩余 {disc} 张唱片），输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterExceptIndex ps.Source "你不能给自己发唱片"
                >> filterKidnapped ps
                >> (if isDisabled then filterDisabled "你的技能在冷却" else id)
    let filter = giveUpOrFilterWith filter
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    
    ps |> sendSkillWith title filter parser
