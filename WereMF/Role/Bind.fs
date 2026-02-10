module WereMF.Role.Bind

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.State
open WereMF.Module.Cli
open WereMF.Role.CaiMon
open WereMF.Role.Creeper
open WereMF.Role.CTF
open WereMF.Role.Doge
open WereMF.Role.Doctor
open WereMF.Role.FaMao
open WereMF.Role.FenXia
open WereMF.Role.HeChong
open WereMF.Role.HuiKa
open WereMF.Role.JiangXian
open WereMF.Role.JiaoHua
open WereMF.Role.Kirby
open WereMF.Role.Leaf
open WereMF.Role.Mole
open WereMF.Role.Myz
open WereMF.Role.PaoXian
open WereMF.Role.Rabi
open WereMF.Role.SheLang
open WereMF.Role.ShiWu
open WereMF.Role.XianSong
open WereMF.Role.YinMo

let rec createRole (r : RollResult) chara : IRole =
    match chara with
    | JiaoHua -> JiaoHuaRole
    | Doge -> DogeRole.New ()
    | Doctor -> DoctorRole.New ()
    | Mole -> MoleRole
    | Rabi -> RabiRole.New ()
    | SheLang -> SheLangRole.New ()
    | FaMao -> FaMaoRole.New ()
    | Kirby -> KirbyRole.New ()
    | FenXia -> FenXiaRole.New ()
    | Creeper -> CreeperRole.New ()
    | PaoXian -> PaoXianRole
    | ShiWu -> ShiWuRole.New ()
    | HuiKa -> HuiKaRole.New ()
    | YinMo -> YinMoRole.New r.BoomCount
    | CTF -> CTFRole.New r.BoomCount
    | HeChong -> HeChongRole.New ()
    | CaiMon -> CaiMonRole.New ()
    | XianSong -> XianSongRole.New ()
    | JiangXian -> JiangXianRole.New ()
    | Myz -> MyzRole.New ()
    | Leaf -> LeafRole.New (r.LeafRolls |> List.map (createRole r))
    
let sendSkill game (ps: PendingSkill) =
    match ps.Type with
    | JiaoHua -> jiaoHuaSendSkill ps game
    | Doge -> dogeSendSkill ps game
    | Doctor -> doctorSendSkill ps game
    | Mole -> moleSendSkill ps game
    | PaoXian -> paoXianSendSkill ps game
    | Kirby -> kirbySendSkill ps game
    | _ -> failwith "not implemented"