module WereMF.Role.Doctor

open System
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

// 解析庸医的输入，格式: "玩家ID1 玩家ID2 ..." 或 "0"
// 返回玩家ID列表，检查是否有重复和数量限制
type DoctorInputResult = 
    | GiveUp
    | Targets of PlayerId list

let parseDoctorInput (maxCount: int) (input: string) : Result<DoctorInputResult, string> =
    let trimmed = input.Trim()
    if trimmed = "0" then
        Ok GiveUp
    else
        match parsePlayerIdList trimmed with
        | Error e -> Error e
        | Ok ids ->
            // 检查数量限制
            if ids.Length > maxCount then
                Error $"药丸数量不足，你只有 {maxCount} 个药丸，不能扎 {ids.Length} 个人"
            elif ids.Length = 0 then
                Ok GiveUp
            else
                // 检查是否有重复
                let distinctIds = ids |> List.distinct
                if distinctIds.Length <> ids.Length then
                    Error "不能重复扎同一个玩家"
                else
                    Ok (Targets ids)

// 庸医技能发送
let doctorSendSkill ps (game: GameContext) =
    let entity = game.GetEntity ps.Source
    let capsuleCount = getCapsuleCount ps.Handler entity
    
    let title = $"输入要扎针的玩家编号（最多 {capsuleCount} 个，空格分隔），输入 0 放弃"
    
    let baseFilter = filterGiveUp
                    >> filterNonExists game
                    >> filterDead game
                    >> filterSelectable game
                    >> filterKidnapped ps
    
    let parser (input: string) : Result<ISkill option list, string> =
        match parseDoctorInput capsuleCount input with
        | Ok GiveUp ->
            // 放弃
            Ok [ None ]
        | Ok (Targets ids) ->
            // 扎多个目标
            let results = ids |> List.map (fun id ->
                match Ok id |> baseFilter with
                | Error e -> Error e
                | Ok _ -> Ok id
            )
            
            // 检查是否有错误
            let errors = results |> List.choose (function Error e -> Some e | _ -> None)
            if errors.Length > 0 then
                Error (errors |> List.head)
            else
                let validIds = results |> List.choose (function Ok id -> Some id | _ -> None)
                let skills = validIds |> List.map (fun id ->
                    { Pending = ps; Target = id } :> ISkill |> Some
                )
                Ok skills
        | Error e -> Error e
    
    ps |> sendSkillWith title baseFilter parser
