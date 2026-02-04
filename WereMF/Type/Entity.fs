module WereMF.Entity

open WereMF.Player
open WereMF.Role

type Entity =
    {
        Player : Player
        Role : IRole
    }