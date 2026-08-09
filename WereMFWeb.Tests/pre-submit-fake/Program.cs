using System.Text.Json;
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
void Send(string api, string target, string content, object? data = null) => Console.WriteLine(JsonSerializer.Serialize(new { api, message_type = target, message_content = content, data }));
object PendingData(string id, object? threaten = null) => new { id, type = "炮仙", source_player_id = 1, priority = 0, kidnapped = false, threaten };
void Pending(string id) => Send("pending_skill_created", "internal", "", PendingData(id));
void Threat(string id, bool force, int target = 3) => Send(force ? "myz_threaten_force_notify" : "myz_threaten_notify", "player_1", force ? $"你被强制威胁技能发给 {target} 号" : $"你被威胁技能发给 {target} 号", new { skill_id = PendingData(id, new { target, force }) });
void Request(string id) => Send("request_paoxian_skill", "player_1", "输入一名玩家的编号令其死亡，输入 0 放弃", new { skill_id = id, invalid_choice = new[] { new { id = 1, reason = "你不能杀死自己" } }, pending_role = (object?)null });
Send("request_player_list", "internal", "输入玩家列表（7~20 人）");
_ = Console.ReadLine();
foreach (var id in new[] { "draft-normal", "draft-armed", "draft-invalid" })
{
    Pending(id); Thread.Sleep(700); Request(id);
    var input = Console.ReadLine() ?? "<eof>";
    Send("pre_submit_test_received", "public", $"{id}:{input}", input);
    Thread.Sleep(150);
}
Pending("threat-normal"); Thread.Sleep(300); Threat("threat-normal", false); Thread.Sleep(100); Request("threat-normal");
var normalThreatInput = Console.ReadLine() ?? "<eof>";
Send("pre_submit_test_received", "public", $"threat-normal:{normalThreatInput}", normalThreatInput);
Thread.Sleep(150);
Pending("threat-force-doge"); Thread.Sleep(300); Threat("threat-force-doge", true); Thread.Sleep(100);
Send("request_doge_skill_force_threaten", "player_1", "你可以选择是否自爆（1：是；0：否）", new { skill_id = PendingData("threat-force-doge", new { target = 3, force = true }), pending_role = (object?)null });
var forceDogeInput = Console.ReadLine() ?? "<eof>";
Send("pre_submit_test_received", "public", $"threat-force-doge:{forceDogeInput}", forceDogeInput);
Thread.Sleep(150);
Pending("threat-force-no-choice"); Thread.Sleep(300); Threat("threat-force-no-choice", true); Thread.Sleep(150);
Send("pre_submit_test_received", "public", "threat-force-no-choice:auto", "auto");
Thread.Sleep(150);
Pending("myz-same"); Thread.Sleep(700);
Send("request_myz_skill", "player_1", "输入要威胁的玩家编号，威胁目标的编号，输入 0 放弃", new { skill_id = "myz-same", invalid_choice = new[] { new { id = 1, reason = "你不能威胁自己" } }, invalid_target_choice = Array.Empty<object>(), pending_role = new { revealed = false } });
var myzInput = Console.ReadLine() ?? "<eof>";
Send("pre_submit_test_received", "public", $"myz-same:{myzInput}", myzInput);
Thread.Sleep(300);
foreach (var id in new[] { "leaf-second-ctf", "leaf-second-xiansong", "leaf-second-creeper" }) Pending(id);
foreach (var id in new[] { "leaf-second-ctf", "leaf-second-xiansong", "leaf-second-creeper" })
{
    Thread.Sleep(100);
    Request(id);
    var input = Console.ReadLine() ?? "<eof>";
    Send("pre_submit_test_received", "public", $"{id}:{input}", input);
}
Thread.Sleep(300);
Send("request_hechong_copy_leaf", "player_2", "选择一个身份复制：1：爬行者；2：CTF；3：贤松");
var copyLeafInput = Console.ReadLine() ?? "<eof>";
if (copyLeafInput is not ("1" or "2" or "3"))
    Send("request_hechong_copy_leaf_parse_error", "player_2", "未知格式", copyLeafInput);
Send("copy_leaf_bot_received", "public", $"copy-leaf-bot:{copyLeafInput}", copyLeafInput);
Thread.Sleep(300);
Send("game_win_broadcast", "public", "游戏结束，测试方获胜");
Thread.Sleep(5000);