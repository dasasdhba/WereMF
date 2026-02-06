module WereMF.Game.Leaf

open WereMF.Chara
open WereMF.Role

type LeafRole =
    {
        Roles : IRole list
        Fury : bool
    }
    interface IRole with
        member this.GetCharaType() = Leaf
        member this.GetPriority() = 100
        member this.GetCopiedRole() =
            if this.Fury then
                this.Roles[1..] |> List.randomChoice
            else
                this.Roles[0]
        member this.GetQueriedChara() =
            (this :> IRole).GetCopiedRole().GetCharaType()
        member this.GetSummaryCharaName() =
            let selects = this.Roles
                            |> List.map (fun r -> r.GetSummaryCharaName())
                            |> String.concat " "
            $"{Leaf.ToString()} （{selects}）"