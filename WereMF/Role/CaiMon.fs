module WereMF.Role.CaiMon

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Role
open WereMF.Module.Cli

type CaiMonRole =
    {
        CaiCount : int
        RebornRound : int option
        RebornList : PlayerId list
    }
    static member New () = { CaiCount = 3 ; RebornRound = None ; RebornList = [] }
    interface IRole with
        member this.Base = {
            CharaType = CaiMon
            Priority = 100
            SummaryName = CaiMon.ToString ()
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            match this.RebornRound with
            | None -> this
            | Some v -> { this with RebornRound = Some (v - 1) }
    interface IRoleGetNightStartDeadRequest with
        member this.Get () =
            let skills =
                if this.RebornRound.IsSome && this.RebornRound.Value <= 0 then
                    [DeadRequest.New Force]
                else
                    []
            if this.CaiCount <= 0 then DeadRequest.New Force :: skills else skills
    interface IRoleGetDayStartDeadRequest with
        member this.Get () =
            if this.CaiCount <= 0 then [ DeadRequest.New Force ] else []
    interface IRolePreventDead with
        member this.Prevent dead = monad {
            if dead = Force || this.RebornRound.IsSome || this.CaiCount <= 1 then None else
            
            let! entity, bind = State.get
            let msg = { Type = ToPlayer entity.Player ; Content = "用一根彩条复活吗？（1：是；0：否）" }
            let yes = requestInputWithMessage msg parseBool
            if yes |> not then None else
            
            { this with RebornRound = Some 1 ; CaiCount = this.CaiCount - 1 } :> IRole |> Some
        }
