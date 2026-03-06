module WereMF.Skill.Bind

open WereMF.Common
open WereMF.Skill.CaiMon
open WereMF.Skill.Creeper
open WereMF.Skill.CTF
open WereMF.Skill.Doge
open WereMF.Skill.Doctor
open WereMF.Skill.FaMao
open WereMF.Skill.FenXia
open WereMF.Skill.HeChong
open WereMF.Skill.HuiKa
open WereMF.Skill.JiangXian
open WereMF.Skill.JiaoHua
open WereMF.Skill.Kirby
open WereMF.Skill.Mole
open WereMF.Skill.Myz
open WereMF.Skill.PaoXian
open WereMF.Skill.Rabi
open WereMF.Skill.SheLang
open WereMF.Skill.ShiWu
open WereMF.Skill.XianSong
open WereMF.Skill.YinMo

let sendSkill game (ps: PendingSkill) =
    match ps.Type with
    | JiaoHua -> jiaoHuaSendSkill ps game
    | Doge -> dogeSendSkill ps game
    | Doctor -> doctorSendSkill ps game
    | Mole -> moleSendSkill ps game
    | PaoXian -> paoXianSendSkill ps game
    | Kirby -> kirbySendSkill ps game
    | HeChong -> heChongSendSkill ps game
    | Rabi -> rabbitSendSkill ps game
    | SheLang -> sheLangSendSkill ps game
    | FaMao -> faMaoSendSkill ps game
    | FenXia -> fenXiaSendSkill ps game
    | Creeper -> creeperSendSkill ps game
    | ShiWu -> shiWuSendSkill ps game
    | HuiKa -> huiKaSendSkill ps game
    | YinMo -> yinMoSendSkill ps game
    | CTF -> ctfSendSkill ps game
    | CaiMon -> caiMonSendSkill ps game
    | XianSong -> xianSongSendSkill ps game
    | JiangXian -> jiangXianSendSkill ps game
    | Myz -> myzSendSkill ps game
    | value -> failwith $"{value.ToString()}不应该发送技能"
