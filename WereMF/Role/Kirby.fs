module WereMF.Role.Kirby

open FSharpPlus
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Skill
open WereMF.Module.Cli

type KirbyRole =
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
            CharaType = Kirby
            Priority = 100
            SummaryName = Kirby.ToString ()
        }
    interface IRoleQueriedHandler with
        member this.Get random =
            match this.CopiedRole with
            | Some role ->
               let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
               (sub |> KirbyHandler).Bind (role |> getQueriedHandler random)
            | None -> IdHandler
    interface IRolePendingHandlers with
        member this.Get player =
            match this.CopiedRole with
            | Some role ->
                let chara = role |> getCharaType
                let msg = {
                    Type = ToPlayer player
                    Content = $"是否使用复制技能（{chara.ToString()}）？（1：使用；0：放弃并使用吸入技能）"
                }

                let yes = requestInputWithMessage msg parseBool
                if yes |> not then [IdHandler] else

                let sub = createSubFunctor
                           (fun k -> k.CopiedRole.Value)
                           (fun v k -> { k with CopiedRole = Some v })
                role |> getPendingHandlers player |> List.map (
                    fun h -> (sub |> KirbyHandler).Bind h)
            | None -> [IdHandler]
    interface IRoleValidHandlers with
        member this.Get () =
            match this.CopiedRole with
                | Some role ->
                    let sub = createSubFunctor
                               (fun k -> k.CopiedRole.Value)
                               (fun v k -> { k with CopiedRole = Some v })
                    let subList = role |> getValidHandlers |> List.map (
                        fun h -> (sub |> KirbyHandler).Bind h)
                    IdHandler :: subList
                | None -> [IdHandler]
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRoleWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            { this with CopiedRole = None }
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            if this.CopiedRole.IsNone then [] else
            let role = this.CopiedRole.Value
            getNightStartDeadRequest role
    interface IRolePreventDead with
        member this.Prevent context dead entity =
            if this.CopiedRole.IsNone then None else
            let role = this.CopiedRole.Value
            let result = role |> tryPreventDead context dead entity
            monad {
                let! r = result
                let role = { this with CopiedRole = Some r.NewRole }
                { r with NewRole = role }
            }

// 卡比吸入技能（当没有复制身份时使用）
let kirbySendSkill ps game =
    let title = "输入一名玩家的编号吸入，输入 0 放弃"
    let filter = filterNonExists game
                >> filterDead game
                >> filterExceptIndex ps.Source "你不能吸入自己"
                >> filterSelectable game
                >> filterKidnapped ps
    let filter = giveUpOrFilterWith filter
    let parser = parsePlayerId >> filter >> Result.map (
        fun r -> if r <= PlayerId 0 then [ None ]
                 else [ { Pending = ps; Target = r } :> ISkill |> Some ])
    ps |> sendSkillWith title filter parser
