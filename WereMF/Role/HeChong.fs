module WereMF.Role.HeChong

open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli

type HeChongRole =
    {
        CopiedRole : IRole option
    }
    static member New () = { CopiedRole = None }
    member private this.UpdateCopiedRoleWith updater =
        match this.CopiedRole with
        | Some role -> { this with CopiedRole = Some (role |> updater) }
        | None -> this
    interface IRole with
        member this.Base = {
            CharaType = HeChong
            Priority = 9
            SummaryName = HeChong.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
               (sub |> CommonHandler).Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with CopiedRole = None }

let heChongSendSkill ps game =
    let title = "输入一名其他玩家的编号复制其身份，输入 0 以放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能复制自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser
