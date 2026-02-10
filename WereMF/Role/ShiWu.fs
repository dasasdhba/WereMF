module WereMF.Role.ShiWu

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type ShiWuRole =
    {
        LastSelected : SelectionState
        Broadcasted : bool
        Exposed : bool
    }
    static member New () = { LastSelected = SelectionState.New () ; Broadcasted = false ; Exposed = false }
    interface IRole with
        member this.Base = {
            CharaType = ShiWu
            Priority = 7
            SummaryName = ShiWu.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            { this with Exposed = false }
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            { this with LastSelected = this.LastSelected.UpdateOnDayStart () }
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with LastSelected = SelectionState.New () }

// 获取上一晚绑架过的玩家列表
let getShiWuLastSelected (handler: RoleHandler) (entity: Entity) : PlayerId list =
    match handler.GetFromEntity entity with
    | :? ShiWuRole as shiWu ->
        match shiWu.LastSelected with
        | SelectionState record -> record.LastNight
    | _ -> []

// 实物技能发送
let shiWuSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let lastSelected = getShiWuLastSelected ps.Handler entity
    
    let title = "输入一名玩家的编号进行绑架，输入 0 放弃"
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "不能绑架自己"
                >> filterSelectable game
                >> filterKidnapped ps
                >> filterExceptIndexList lastSelected "不能连续绑架同一个玩家"
    let filter = giveUpOrFilterWith filter
    
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    
    ps |> sendSkillWith title filter parser
