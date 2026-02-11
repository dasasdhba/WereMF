namespace WereMF.Common

type RoleFunctor =
    {
        /// OwnerRole -> Role
        Getter : IRole -> IRole
        /// newRole -> OwnerRole -> UpdatedRole
        Setter : IRole -> IRole -> IRole
    }
    member this.Bind (functor : RoleFunctor) =
        {
            Getter = this.Getter >> functor.Getter
            Setter = (fun n owner ->
                owner |> this.Setter (owner |> this.Getter |> functor.Setter n))
        }
    static member Default =
        {
            Getter = id
            Setter = (fun n _ -> n)
        }

type RoleHandler =
    | IdHandler
    | CommonHandler of RoleFunctor
    | KirbyHandler of RoleFunctor
    member private this.Functor =
        match this with
        | IdHandler -> RoleFunctor.Default
        | CommonHandler roleFunctor -> roleFunctor
        | KirbyHandler roleFunctor -> roleFunctor
    member this.Getter =
        this.Functor.Getter
    member this.Setter =
        this.Functor.Setter
    member this.GetFromEntity entity =
        entity.Role |> this.Getter
    member this.SetToEntity role entity =
        { entity with Role = entity.Role |> this.Setter role }
    member this.Bind (handler : RoleHandler) =
        let bindFunctor () =
            this.Functor.Bind handler.Functor
        match handler with
        | IdHandler -> this
        | CommonHandler _ -> bindFunctor () |> CommonHandler
        | KirbyHandler _ -> bindFunctor () |> KirbyHandler

type ThreatenSkill = {
    Source : PlayerId
    Target : PlayerId
    Force : bool
}

type PendingSkill = {
    Handler : RoleHandler
    Type : CharaType
    Source : PlayerId
    Priority : int
    Threaten : ThreatenSkill option
    Kidnapped : bool
}

type SpringType =
    | Normal
    | Recursed

type SendingSkill =
    {
        Pending : PendingSkill
        Target : PlayerId
        Spring : SpringType option
    }

type ISkill = interface end

type Skill =
    {
        Sending : SendingSkill
        Actor : ISkill
    }
    static member New pending target actor =
        {
            Sending = {
                Pending = pending
                Target = target
                Spring = None
            }
            Actor = actor
        }