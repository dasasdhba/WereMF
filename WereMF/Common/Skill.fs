namespace WereMF.Common

type RoleHandler =
    {
        /// OwnerRole -> Role
        Getter : IRole -> IRole
        /// newRole -> OwnerRole -> UpdatedRole
        Setter : IRole -> IRole -> IRole
    }
    member this.GetFromEntity entity =
        entity.Role |> this.Getter
    member this.SetToEntity role entity =
        { entity with Role = entity.Role |> this.Setter role }
    static member Default =
        {
            Getter = id
            Setter = (fun role _ -> role)
        }

type PendingSkill = {
    Handler : RoleHandler
    Type : CharaType
    Source : PlayerId
    Priority : int
    /// (target, isForce)
    Threaten : (PlayerId * bool) option
    Kidnapped : bool
    Blocked : bool
    FromKirby : bool
}

type Skill = {
    Pending : PendingSkill
    Target : PlayerId
}