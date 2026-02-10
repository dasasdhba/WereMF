module WereMF.Role.FenXia

open FSharpPlus
open WereMF.Common
open WereMF.Module.Role

type FenXiaRole =
    {
        FenCount : int
        CopiedRoles : IRole list
        Reborn : bool
    }
    static member New () = { FenCount = 3 ; CopiedRoles = [] ; Reborn = false }
    member private this.UpdateCopiedRolesWith updater =
        { this with CopiedRoles = this.CopiedRoles |> List.map updater }
    interface IRole with
        member this.Base = {
            CharaType = FenXia
            Priority = 100
            SummaryName = FenXia.ToString ()
        }
    interface IRolePendingHandlers with
        member this.Get player = monad {
            let mutable result = [IdHandler]
            for i in 0..(this.CopiedRoles.Length - 1) do
                let role = this.CopiedRoles[i]
                let! hs = role |> getPendingHandlers player
                let sub = createSubFunctor
                               (fun k -> k.CopiedRoles[i])
                               (fun v k ->
                     { k with CopiedRoles = k.CopiedRoles |> List.updateAt i v })
                result <- result @ (hs |> List.map (fun h -> (sub |> CommonHandler).Bind h))
            result
        }
    interface IRoleUpdateOnNightStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnNightStart
    interface IRoleUpdateOnDayStart with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDayStart
    interface IRoleUpdateOnDead with
        member this.Update () =
            this.UpdateCopiedRolesWith updateOnDead
