module WereMF.Game.Leaf

open FSharpPlus
open FSharpPlus.Data
open WereMF.Type.Chara
open WereMF.Type.Role

type LeafRole (roles : Role list, ?fury : bool) =
    inherit Role()
    member this.Roles = roles
    member val Fury = defaultArg fury false with get, set
    
    override this.GetCharaType() = Leaf
    override this.GetPriority() = 100
    override this.GetCopiedRole() = monad {
        let! rng = Reader.ask
        if this.Fury then
            this.Roles[1..] |> List.randomChoiceWith rng
        else
            this.Roles[0]
    }
    override this.GetRabiRole() = monad {
        let! role = this.GetCopiedRole()
        return! role.GetRabiRole ()
    }
    override this.GetSummaryCharaName() =
        let selects = this.Roles
                        |> List.map (fun r -> r.GetSummaryCharaName())
                        |> String.concat " "
        $"{Leaf.ToString()}（{selects}）"