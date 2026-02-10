module WereMF.Role.Doctor

open WereMF.Common
open WereMF.Module.Skill
open WereMF.Module.Cli
open WereMF.State

type DoctorRole =
    {
        Capsule : int
    }
    static member New () = { Capsule = 4 }
    interface IRole with
        member this.Base = {
            CharaType = Doctor
            Priority = 0
            SummaryName = Doctor.ToString ()
        }

// 获取剩余药丸数量
let getCapsuleCount (handler: RoleHandler) (entity: Entity) : int =
    match handler.GetFromEntity entity with
    | :? DoctorRole as doctorRole -> doctorRole.Capsule
    | _ -> 0

// 庸医技能发送
let doctorSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let capsuleCount = getCapsuleCount ps.Handler entity
    
    let title = $"输入要扎针的玩家编号（最多 {capsuleCount} 个），输入 0 放弃"
    
    let config = {
        MaxCount = capsuleCount
        MaxCountError = Some $"药丸数量不足，你只有 {capsuleCount} 个药丸"
        DuplicateError = Some "不能重复扎同一个玩家"
    }
    
    let filter = filterNonExists game
                >> filterDead game
                >> filterSelectable game
                >> filterKidnapped ps
                >> (if capsuleCount = 0 then filterDisabled "你没有药丸了" else id)
    let filter = giveUpOrFilterWith filter
    
    let createSkill id = { Pending = ps; Target = id } :> ISkill
    let parser = parseMultiSkill config filter createSkill
    ps |> sendSkillWith title filter parser
