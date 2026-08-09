using System.Text;
using System.Text.Json;
Console.OutputEncoding = new UTF8Encoding(false);
void Send(string api, string target, string content, object? data = null) => Console.WriteLine(JsonSerializer.Serialize(new { api, message_type = target, message_content = content, data }));
Send("request_player_list", "internal", "输入玩家列表（7~20 人）");
_ = Console.ReadLine();
var players = Enumerable.Range(1, 7).Select(id => new { id, name = $"P{id}", anonymous = false }).ToArray();
Send("player_init", "public", "players", players);
Send("day_start_broadcast", "public", "白天开始");
var entities = Enumerable.Range(1, 7).Select(id => new {
  player = new { id, name = $"P{id}", anonymous = false }, role = (object?)null,
  state = new { is_dead = id == 2, is_dead_public = id == 2, dead_showing_name = "", myz_threaten = id == 1, is_bar_leader = false, reversed = false, smog_count = 0, capsule_count = 0, potion_count = 0, xian_song_count = 0, bug_count = 0, jiaohua_vote_blocked = false, shiwu_kidnapped = false, jiaohua_protected = false, jiaohua_blocked = 0, leaf_protected = false }
}).ToArray();
Send("game_update_day", "public", "", entities);
Send("vote_start_broadcast", "public", "投票开始");
var voteData = Enumerable.Range(1, 7).Select(id => new {
  id,
  can_vote = id != 2,
  can_suicide = false,
  invalid_vote = Array.Empty<object>()
}).ToArray();
var botVotes = new List<string>();
for (var i = 0; i < 10; i++) {
  Send("request_vote", "public", "请选择你的投票", voteData);
  botVotes.Add(Console.ReadLine() ?? "");
}
Send("random_bot_votes_received", "public", string.Join("|", botVotes), botVotes);
Thread.Sleep(TimeSpan.FromSeconds(5));
