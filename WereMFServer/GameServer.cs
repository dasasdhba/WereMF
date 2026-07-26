using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WereMFServer;

internal sealed class GameServer : IDisposable
{
    private readonly ServerOptions _options;
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    public int ActiveRoomCount => _rooms.Count;
    public GameServer(ServerOptions options) => _options = options;
    public object GetPublicRooms() => _rooms.Values.Where(x => x.IsJoinable).Select(x => x.PublicState()).ToArray();

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        GameRoom? room = null; PlayerSession? player = null;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var text = await ReceiveAsync(socket, ct); if (text is null) break;
                try
                {
                    var msg = JsonSerializer.Deserialize<ClientMessage>(text, JsonOptions) ?? new();
                    if (player is null) (room, player) = await AttachAsync(socket, msg, ct);
                    else if (msg.Type == "leave_room")
                    {
                        await room!.LeaveAsync(player);
                        await SendRawAsync(socket, new { type = "left_room" }, ct);
                        room = null; player = null;
                        if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "left room", ct);
                        break;
                    }
                    else await room!.HandleAsync(player, msg, ct);
                }
                catch (ClientVisibleException e) { await SendRawAsync(socket, new { type = "error", message = e.Message }, ct); }
                catch (JsonException) { await SendRawAsync(socket, new { type = "error", message = "消息格式无效" }, ct); }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally { if (room is not null && player is not null) await room.DisconnectAsync(player); }
    }

    private async Task<(GameRoom, PlayerSession)> AttachAsync(WebSocket socket, ClientMessage msg, CancellationToken ct)
    {
        var name = (msg.PlayerName ?? "").Trim();
        if (name.Length is < 1 or > 20) throw new ClientVisibleException("昵称需要 1–20 个字符");
        if (msg.Type == "create_room")
        {
            var room = CreateRoom(); return (room, await room.CreateHostAsync(socket, name, ct));
        }
        if (msg.Type is "join_room" or "reconnect")
        {
            var code = (msg.RoomCode ?? "").Trim();
            if (!_rooms.TryGetValue(code, out var room)) throw new ClientVisibleException("房间不存在或已经结束");
            return (room, await room.JoinAsync(socket, name, msg.Token, ct));
        }
        throw new ClientVisibleException("请先创建或加入房间");
    }

    private GameRoom CreateRoom()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            var room = new GameRoom(code, _options, c => { _rooms.TryRemove(c, out _); return Task.CompletedTask; });
            if (_rooms.TryAdd(code, room)) return room;
        }
        throw new InvalidOperationException("No room code available");
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static async Task SendRawAsync(WebSocket socket, object data, CancellationToken ct = default)
    {
        if (socket.State == WebSocketState.Open) await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions), WebSocketMessageType.Text, true, ct);
    }
    private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192]; using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(buffer, ct); if (result.MessageType == WebSocketMessageType.Close) return null; stream.Write(buffer, 0, result.Count); }
        while (!result.EndOfMessage && stream.Length < 1_048_576);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public void Dispose() { foreach (var room in _rooms.Values) room.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
}

internal sealed class GameRoom : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _code; private readonly ServerOptions _options; private readonly Func<string, Task> _remove;
    private readonly List<PlayerSession> _players = []; private readonly List<JsonElement> _publicHistory = []; private readonly List<JsonElement> _hostHistory = [];
    private GameProcess? _game; private bool _started; private bool _exportingLog; private string? _expected;
    private ConcurrentInputPhase? _concurrentInput;
    private readonly SemaphoreSlim _routeLock = new(1, 1);
    private CancellationTokenSource? _timerCts; private long _timerVersion; private int _aliveCount; private long _gameVersion;
    public bool IsJoinable => !_started && _players.Count < 16;
    public GameRoom(string code, ServerOptions options, Func<string, Task> remove) => (_code, _options, _remove) = (code, options, remove);
    public object PublicState() => new { code = _code, players = _players.Count, maxPlayers = 16, started = _started };

    public async Task<PlayerSession> CreateHostAsync(WebSocket socket, string name, CancellationToken ct)
    {
        var p = new PlayerSession(1, name, true, socket); lock (_gate) _players.Add(p); await WelcomeAsync(p, ct); await StateAsync(ct); return p;
    }
    public async Task<PlayerSession> JoinAsync(WebSocket socket, string name, string? token, CancellationToken ct)
    {
        PlayerSession p;
        lock (_gate)
        {
            p = token is null ? null! : _players.FirstOrDefault(x => x.Token == token && !x.HasLeft)!;
            if (p is not null) { p.Socket = socket; p.Connected = true; if (!p.IsPermanentBot) { p.IsBot = false; p.MissedRequests = 0; } }
            else
            {
                if (_started) throw new ClientVisibleException("对局已经开始，请使用原设备重连");
                if (_players.Count >= 16) throw new ClientVisibleException("房间已满");
                if (_players.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ClientVisibleException("昵称已被使用");
                p = new PlayerSession(_players.Count + 1, name, false, socket); _players.Add(p);
            }
        }
        await WelcomeAsync(p, ct); await ReplayAsync(p, ct); await StateAsync(ct); return p;
    }

    public async Task HandleAsync(PlayerSession p, ClientMessage msg, CancellationToken ct)
    {
        if (msg.Type == "start_game") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以开始"); await StartAsync(ct); return; }
        if (msg.Type == "add_bot") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以增加 Bot"); await AddBotAsync(ct); return; }
        if (msg.Type == "remove_bot") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以删除 Bot"); await RemoveBotAsync(ct); return; }
        if (msg.Type == "restart_room") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以重开"); await RestartRoomAsync(ct); return; }
        if (msg.Type == "game_input") { await InputAsync(p, msg.Value ?? "", ct); return; }
        if (msg.Type == "pending_draft") { SaveDraft(p, msg); return; }
        if (msg.Type == "command")
        {
            if (!p.IsHost || _game is null) throw new ClientVisibleException("只有房主可以使用管理命令");
            if (msg.Value != "\\restart") throw new ClientVisibleException("房主只能使用重开命令");
            await RestartRoomAsync(ct);
            return;
        }
        if (msg.Type == "ping") { await SendAsync(p, new { type = "pong" }, ct); return; }
        throw new ClientVisibleException("未知操作");
    }

    public async Task LeaveAsync(PlayerSession player)
    {
        var removeRoom = false;
        var becameBot = false;
        PlayerSession[] remaining;
        lock (_gate)
        {
            player.Connected = false;
            player.Socket = null;
            if (_started)
            {
                player.HasLeft = true;
                player.IsBot = true;
                player.MissedRequests = 0;
                becameBot = true;
            }
            else
            {
                _players.Remove(player);
                for (var i = 0; i < _players.Count; i++) _players[i].Id = _players[i].GameId = i + 1;
            }
            if (player.IsHost)
            {
                player.IsHost = false;
                var candidates = _players.Where(x => x != player && x.Connected && !x.IsBot && !x.HasLeft).ToArray();
                if (candidates.Length > 0) candidates[Random.Shared.Next(candidates.Length)].IsHost = true;
                else if (!_started) removeRoom = true;
            }
            remaining = _players.Where(x => x.Connected && !x.IsPermanentBot && !x.HasLeft).ToArray();
        }
        if (removeRoom) { await _remove(_code); return; }
        if (becameBot)
            await BroadcastAsync(new { type = "bot_takeover", playerId = player.GameId, playerName = player.Name, message = $"{player.Name} 已彻底退出，本局剩余操作由 Bot 接管" });
        await Task.WhenAll(remaining.Select(x => SendAsync(x, new { type = "session_state", playerId = x.GameId, isHost = x.IsHost })));
        await StateAsync();
    }
    private async Task RestartRoomAsync(CancellationToken ct)
    {
        GameProcess? game;
        await _routeLock.WaitAsync(ct);
        try
        {
            lock (_gate)
            {
                if (!_started || _game is null) throw new ClientVisibleException("当前没有正在进行的对局");
                _gameVersion++;
                game = _game; _game = null; _started = false; _exportingLog = false; _expected = null; _concurrentInput = null; _aliveCount = 0;
                CancelTimerLocked();
                _publicHistory.Clear(); _hostHistory.Clear();
                _players.RemoveAll(x => x.HasLeft);
                for (var i = 0; i < _players.Count; i++)
                {
                    var player = _players[i];
                    player.Id = player.GameId = i + 1; player.MissedRequests = 0; player.History.Clear(); player.Drafts.Clear();
                    if (!player.IsPermanentBot) player.IsBot = false;
                }
            }
        }
        finally { _routeLock.Release(); }
        await game.DisposeAsync();
        await BroadcastAsync(new { type = "room_restarted", message = "房主已结束本局，返回等待大厅" }, ct);
        await StateAsync(ct);
    }

    private async Task AddBotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局开始后不能增加 Bot");
            if (_players.Count >= 16) throw new ClientVisibleException("房间已满");
            var number = 1;
            while (_players.Any(x => x.Name.Equals($"Bot {number}", StringComparison.OrdinalIgnoreCase))) number++;
            _players.Add(new PlayerSession(_players.Count + 1, $"Bot {number}", false, null) { IsBot = true, IsPermanentBot = true });
        }
        await StateAsync(ct);
    }

    private async Task RemoveBotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局开始后不能删除 Bot");
            var bot = _players.LastOrDefault(x => x.IsPermanentBot);
            if (bot is null) throw new ClientVisibleException("房间里没有 Bot");
            _players.Remove(bot);
            for (var i = 0; i < _players.Count; i++) _players[i].Id = _players[i].GameId = i + 1;
        }
        await StateAsync(ct);
    }

    private void SaveDraft(PlayerSession player, ClientMessage msg)
    {
        var key = !string.IsNullOrWhiteSpace(msg.SkillId) ? $"skill:{msg.SkillId.Trim()}" : !string.IsNullOrWhiteSpace(msg.Api) ? $"api:{msg.Api.Trim()}" : "";
        var value = (msg.Value ?? "").Trim();
        if (key.Length is 0 or > 100 || value.Length > 240) throw new ClientVisibleException("预选草稿无效");
        lock (_gate)
        {
            if (player.Drafts.Count >= 64 && !player.Drafts.ContainsKey(key)) player.Drafts.Remove(player.Drafts.Keys.First());
            player.Drafts[key] = value;
        }
    }
    private async Task StartAsync(CancellationToken ct)
    {
        PlayerSession[] players; GameProcess game; long version;
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局已经开始");
            if (_players.Count < 7) throw new ClientVisibleException("至少需要 7 名玩家");
            _started = true; players = _players.ToArray(); version = ++_gameVersion;
            _game = game = new GameProcess(_options.GamePath, _options.Config, _options.Seed);
        }
        game.OutputReceived += line => RouteAsync(line, version);
        game.Exited += code => version == Interlocked.Read(ref _gameVersion) && _started
            ? BroadcastAsync(new { type = "game_ended", message = code == 0 ? "对局已结束" : "规则进程意外退出" })
            : Task.CompletedTask;
        game.Start();
        await StateAsync(ct);
        await game.SendAsync(string.Join(" ", players.Select(x => $"\"{x.Name.Replace("\"", "”")}\"")), ct);
    }
    private async Task InputAsync(PlayerSession p, string value, CancellationToken ct)
    {
        if (_game is null) throw new ClientVisibleException("对局尚未开始");
        if (p.IsBot) throw new ClientVisibleException("该席位当前由 Bot 托管");
        value = value.Trim();
        if (value.StartsWith('\\')) throw new ClientVisibleException("玩家输入不能使用管理命令");
        if (_concurrentInput is not null)
        {
            await ConcurrentInputAsync(p, value, ct);
            return;
        }
        lock (_gate)
        {
            var allowed = _expected == $"player_{p.GameId}" || _expected == "public" || (_expected == "internal" && p.IsHost);
            if (!allowed) throw new ClientVisibleException("现在还没轮到你行动");
            p.MissedRequests = 0;
            _expected = null;
            CancelTimerLocked();
        }
        await _game.SendAsync(value, ct);
    }

    private async Task ConcurrentInputAsync(PlayerSession player, string value, CancellationToken ct)
    {
        string? cliInput;
        JsonElement? repeatPrompt = null;
        ConcurrentInputPhase? reschedule = null;
        int remaining;
        string api;
        lock (_gate)
        {
            var phase = _concurrentInput ?? throw new ClientVisibleException("当前并非并发输入阶段");
            if (player.IsBot) throw new ClientVisibleException("该席位当前由 Bot 托管");
            api = phase.Api;
            if (!phase.Remaining.TryGetValue(player.GameId, out remaining) || remaining <= 0)
                throw new ClientVisibleException("你在这个阶段的提交次数已经用完");
            var trimmed = value.Trim();
            if (phase.Api == "request_reroll_player")
            {
                if (trimmed is not ("0" or "1")) throw new ClientVisibleException("请选择保留或重抽身份");
                if (trimmed == "1") phase.Queue.Enqueue((player.GameId, player.GameId.ToString()));
            }
            else
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (!int.TryParse(parts[0], out var actor) || actor != player.GameId)
                        throw new ClientVisibleException("只能提交自己的投票");
                    trimmed = parts[1];
                }
                if (trimmed.Equals("b", StringComparison.OrdinalIgnoreCase))
                {
                    if (!phase.CanSuicide.Contains(player.GameId)) throw new ClientVisibleException("你当前不能自爆");
                    phase.Queue.Enqueue((player.GameId, $"{player.GameId} b"));
                }
                else
                {
                    if (!int.TryParse(trimmed, out var target) || target < 0 || target > _players.Count)
                        throw new ClientVisibleException("投票目标无效");
                    if (phase.InvalidVotes.TryGetValue(player.GameId, out var invalid) && invalid.Contains(target))
                        throw new ClientVisibleException("当前不能投给这名玩家");
                    phase.Queue.Enqueue((player.GameId, $"{player.GameId} {target}"));
                }
            }
            player.MissedRequests = 0;
            phase.Responded.Add(player.GameId);
            remaining = --phase.Remaining[player.GameId];
            if (remaining > 0) repeatPrompt = phase.Prompt;
            if (phase.Api == "request_vote" && !phase.TimedOut)
            {
                phase.Deadline = phase.Deadline.AddSeconds(-_options.VotePenaltySeconds);
                reschedule = phase;
            }
            cliInput = TakeConcurrentCliInputLocked(phase);
        }
        await SendAsync(player, new { type = "input_accepted", api, remaining }, ct);
        if (repeatPrompt is JsonElement prompt) await SendConcurrentPromptAsync(player, prompt, remaining, ct);
        if (reschedule is not null) await ScheduleConcurrentTimerAsync(reschedule);
        if (cliInput is not null) await _game!.SendAsync(cliInput, ct);
    }

    private static string? TakeConcurrentCliInputLocked(ConcurrentInputPhase phase)
    {
        if (!phase.CliWaiting) return null;
        if (phase.Queue.TryDequeue(out var queued))
        {
            phase.CliWaiting = false;
            return queued.Value;
        }
        if (phase.Remaining.Values.All(x => x == 0) &&
            (phase.Api == "request_reroll_player" || phase.TimedOut))
        {
            phase.CliWaiting = false;
            return "0";
        }
        return null;
    }

    private bool RegisterMissedRequestLocked(PlayerSession? player)
    {
        if (player is null || player.Connected || player.IsPermanentBot || player.IsBot) return false;
        player.MissedRequests++;
        if (player.MissedRequests < 2) return false;
        player.IsBot = true;
        return true;
    }

    private async Task AnnounceBotTakeoverAsync(PlayerSession player)
    {
        await BroadcastAsync(new { type = "bot_takeover", playerId = player.GameId, message = $"{player.Name}连续两次未响应，断线期间已由 Bot 临时托管" });
        await StateAsync();
    }

    private async Task ResolveBotRequestAsync(JsonElement root, string api, string target, PlayerSession player)
    {
        lock (_gate)
        {
            if (_expected != target || !player.IsBot) return;
            _expected = null;
            CancelTimerLocked();
        }
        var value = RandomLegalInput(root, api);
        if (_game is not null) await _game.SendAsync(value);
    }

    private void ResolveConcurrentBotsLocked(ConcurrentInputPhase phase)
    {
        foreach (var player in _players.Where(x => x.IsBot && phase.Remaining.TryGetValue(x.GameId, out var count) && count > 0))
        {
            var remaining = phase.Remaining[player.GameId];
            if (phase.Api == "request_reroll_player")
            {
                if (RandomNumberGenerator.GetInt32(2) == 1) phase.Queue.Enqueue((player.GameId, player.GameId.ToString()));
            }
            else
            {
                for (var i = 0; i < remaining; i++)
                {
                    var choices = Enumerable.Range(0, _players.Count + 1)
                        .Except(phase.InvalidVotes.TryGetValue(player.GameId, out var invalid) ? invalid : [])
                        .Select(x => x.ToString()).ToList();
                    if (i == remaining - 1 && phase.CanSuicide.Contains(player.GameId)) choices.Add("b");
                    var choice = choices[RandomNumberGenerator.GetInt32(choices.Count)];
                    phase.Queue.Enqueue((player.GameId, $"{player.GameId} {choice}"));
                    if (!phase.TimedOut) phase.Deadline = phase.Deadline.AddSeconds(-_options.VotePenaltySeconds);
                }
            }
            phase.Remaining[player.GameId] = 0;
        }
    }

    private void CancelTimerLocked()
    {
        _timerVersion++;
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
    }

    private async Task StartRegularTimerAsync(JsonElement root, string api, string target)
    {
        CancellationToken token; long version; DateTimeOffset deadline;
        lock (_gate)
        {
            CancelTimerLocked();
            _timerCts = new CancellationTokenSource(); token = _timerCts.Token; version = _timerVersion;
            deadline = DateTimeOffset.UtcNow.AddSeconds(_options.RequestTimeoutSeconds);
        }
        await SendToTargetAsync(target, new { type = "request_timer", api, deadlineUtc = deadline.ToUnixTimeMilliseconds(), mode = "request" });
        _ = RunRegularTimeoutAsync(root, api, target, deadline, version, token);
    }

    private async Task RunRegularTimeoutAsync(JsonElement root, string api, string target, DateTimeOffset deadline, long version, CancellationToken token)
    {
        try { var delay = deadline - DateTimeOffset.UtcNow; if (delay > TimeSpan.Zero) await Task.Delay(delay, token); }
        catch (OperationCanceledException) { return; }
        PlayerSession? player; bool takeover;
        lock (_gate)
        {
            if (version != _timerVersion || _expected != target) return;
            _expected = null; player = PlayerForTarget(target); takeover = RegisterMissedRequestLocked(player); CancelTimerLocked();
        }
        if (takeover && player is not null) await AnnounceBotTakeoverAsync(player);
        var (value, source) = ResolveTimeoutInput(root, api, player);
        var notice = new { type = "request_timeout_resolved", api, value, source, message = source == "draft" ? $"操作超时，已采用你的预选：{value}" : $"操作超时，系统已随机选择：{value}" };
        await SendToTargetAsync(target, notice);
        if (_game is not null) await _game.SendAsync(value);
    }

    private async Task ScheduleConcurrentTimerAsync(ConcurrentInputPhase phase)
    {
        CancellationToken token; long version;
        lock (_gate)
        {
            if (_concurrentInput != phase || phase.TimedOut) return;
            CancelTimerLocked(); _timerCts = new CancellationTokenSource(); token = _timerCts.Token; version = _timerVersion;
        }
        var timer = new { type = "request_timer", api = phase.Api, deadlineUtc = phase.Deadline.ToUnixTimeMilliseconds(), mode = phase.Api == "request_vote" ? "vote" : "request" };
        if (phase.Api == "request_vote") await BroadcastAsync(timer);
        else foreach (var id in phase.Remaining.Keys) { var p = _players.FirstOrDefault(x => x.GameId == id); if (p is not null) await SendAsync(p, timer); }
        _ = RunConcurrentTimeoutAsync(phase, version, token);
    }

    private async Task RunConcurrentTimeoutAsync(ConcurrentInputPhase phase, long version, CancellationToken token)
    {
        try { var delay = phase.Deadline - DateTimeOffset.UtcNow; if (delay > TimeSpan.Zero) await Task.Delay(delay, token); }
        catch (OperationCanceledException) { return; }
        var privateNotices = new List<(PlayerSession Player, object Notice)>(); var takeovers = new List<PlayerSession>(); string? cliInput;
        lock (_gate)
        {
            if (version != _timerVersion || _concurrentInput != phase || phase.TimedOut) return;
            phase.TimedOut = true; CancelTimerLocked();
            foreach (var (id, remaining) in phase.Remaining.ToArray())
            {
                if (remaining <= 0) continue;
                var player = _players.FirstOrDefault(x => x.GameId == id); if (player is null) continue;
                if (!phase.Responded.Contains(id) && RegisterMissedRequestLocked(player)) takeovers.Add(player);
                if (phase.Api == "request_reroll_player")
                {
                    var yes = RandomNumberGenerator.GetInt32(2) == 1;
                    if (yes) phase.Queue.Enqueue((id, id.ToString()));
                    privateNotices.Add((player, new { type = "request_timeout_resolved", api = phase.Api, value = yes ? "1" : "0", source = "random", message = yes ? "操作超时，系统随机选择了重抽身份" : "操作超时，系统随机选择了保留身份" }));
                }
                phase.Remaining[id] = 0;
            }
            cliInput = TakeConcurrentCliInputLocked(phase);
        }
        foreach (var player in takeovers) await AnnounceBotTakeoverAsync(player);
        foreach (var (player, notice) in privateNotices) await SendAsync(player, notice);
        if (phase.Api == "request_vote")
            await BroadcastAsync(new { type = "request_timeout_resolved", api = phase.Api, value = "0", source = "cli_default", message = "投票时间结束，未完成的玩家由 CLI 按默认弃票处理" });
        if (cliInput is not null && _game is not null) await _game.SendAsync(cliInput);
    }

    private PlayerSession? PlayerForTarget(string target)
    {
        if (target.StartsWith("player_") && int.TryParse(target[7..], out var id)) return _players.FirstOrDefault(x => x.GameId == id);
        if (target == "internal") return _players.FirstOrDefault(x => x.IsHost);
        return null;
    }

    private Task SendToTargetAsync(string target, object data)
    {
        if (target == "public") return BroadcastAsync(data);
        var player = PlayerForTarget(target); return player is null ? Task.CompletedTask : SendAsync(player, data);
    }

    private (string Value, string Source) ResolveTimeoutInput(JsonElement root, string api, PlayerSession? player)
    {
        var skillId = ExtractSkillId(root);
        string? draft = null;
        lock (_gate)
        {
            if (player is not null && skillId is not null) player.Drafts.TryGetValue($"skill:{skillId}", out draft);
            if (player is not null && string.IsNullOrWhiteSpace(draft)) player.Drafts.TryGetValue($"api:{api}", out draft);
        }
        if (!string.IsNullOrWhiteSpace(draft) && IsLegalTimeoutInput(root, api, draft)) return (draft, "draft");
        return (RandomLegalInput(root, api), "random");
    }

    private static string? ExtractSkillId(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("skill_id", out var id)) return null;
        if (id.ValueKind == JsonValueKind.String) return id.GetString();
        return id.ValueKind == JsonValueKind.Object && id.TryGetProperty("id", out var nested) ? nested.GetString() : null;
    }

    private bool IsLegalTimeoutInput(JsonElement root, string api, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (api == "request_leaf_charas") return IsLegalLeafChoice(root, parts);
        if (api == "request_xiansong_skill_force_threaten") return parts.Length == 1 && parts[0] is "m" or "x" or "0";
        if (api.Contains("force_threaten") && api != "request_myz_skill_force_threaten") return parts.Length == 1 && parts[0] is "0" or "1";
        if (IsBooleanRequest(api)) return parts.Length == 1 && parts[0] is "0" or "1";
        if (api == "request_hechong_copy_leaf") return parts.Length == 1 && RoleNames.Contains(parts[0]) && parts[0] is not ("叶子" or "合虫");
        var ids = parts.Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
        if (ids.Length == 0) return false;
        var invalid = InvalidChoices(root, "invalid_choice");
        if (ids.Any(x => x < 0 || x > _players.Count || invalid.Contains(x))) return false;
        if (api == "request_myz_skill" && ids.Length < 2) return false;
        if (api == "request_rabi_skill" && !parts.Any(x => x is "x" or "d")) return false;
        if (api == "request_jiaohua_dead_skill" && !parts.Any(x => x is "x" or "p")) return false;
        return true;
    }

    private string RandomLegalInput(JsonElement root, string api)
    {
        if (api == "request_leaf_charas") return RandomLeafChoice(root);
        if (api == "request_xiansong_skill_force_threaten") return RandomChoice(["m", "x", "0"]);
        if (api.Contains("force_threaten") && api != "request_myz_skill_force_threaten") return RandomNumberGenerator.GetInt32(2).ToString();
        if (IsBooleanRequest(api)) return RandomNumberGenerator.GetInt32(2).ToString();
        if (api == "request_hechong_copy_leaf") return RandomChoice(RoleNames.Where(x => x is not ("叶子" or "合虫")).ToArray());
        var valid = Enumerable.Range(1, _players.Count).Except(InvalidChoices(root, "invalid_choice")).ToArray();
        if (valid.Length == 0) return "0";
        var first = valid[RandomNumberGenerator.GetInt32(valid.Length)];
        if (api == "request_myz_skill")
        {
            var targets = Enumerable.Range(1, _players.Count).Except(InvalidChoices(root, "invalid_target_choice")).Where(x => x != first).ToArray();
            return targets.Length == 0 ? "0" : $"{first} {targets[RandomNumberGenerator.GetInt32(targets.Length)]}";
        }
        if (api == "request_rabi_skill") return $"{first} {(RandomNumberGenerator.GetInt32(2) == 0 ? "x" : "d")}";
        if (api == "request_jiaohua_dead_skill") return $"{first} {(RandomNumberGenerator.GetInt32(2) == 0 ? "x" : "p")}";
        return first.ToString();
    }

    private static HashSet<int> InvalidChoices(JsonElement root, string property)
    {
        var result = new HashSet<int>(); if (!root.TryGetProperty("data", out var data)) return result;
        JsonElement list;
        if (data.ValueKind == JsonValueKind.Array) list = data;
        else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var nested)) list = nested;
        else return result;
        if (list.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var direct)) result.Add(direct);
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.TryGetInt32(out var value)) result.Add(value);
        }
        return result;
    }

    private static bool IsBooleanRequest(string api) => api.EndsWith("_reborn") || api is "request_anonymous_game" or "request_leaf_game" or "request_leaf_chara_reroll" or "request_drink_milk" or "request_xiansong_give_mfa" or "request_kirby_using_copy_skill" or "request_mole_red_ground" or "request_for_next_game";
    private static readonly string[] RoleNames = ["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"];
    private static string RandomChoice(string[] values) => values[RandomNumberGenerator.GetInt32(values.Length)];

    private static bool IsLegalLeafChoice(JsonElement root, string[] values)
    {
        if (values.Length != 4 || values.Distinct().Count() != 4 || !root.TryGetProperty("data", out var data) || !data.TryGetProperty("options", out var options)) return false;
        var camps = options.EnumerateArray().ToDictionary(x => x.GetProperty("value").GetString()!, x => x.GetProperty("camp").GetString()!);
        return values.All(camps.ContainsKey) && values.Select(x => camps[x]).Distinct().Count() >= 2;
    }

    private static string RandomLeafChoice(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("options", out var options)) return "脚滑人 Doge 炮仙 实物";
        var rows = options.EnumerateArray().Select(x => (Value: x.GetProperty("value").GetString()!, Camp: x.GetProperty("camp").GetString()!)).ToArray();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var selected = rows.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(4).ToArray();
            if (selected.Select(x => x.Camp).Distinct().Count() >= 2) return string.Join(' ', selected.Select(x => x.Value));
        }
        return "脚滑人 Doge 炮仙 实物";
    }
    private async Task RouteAsync(string line, long version)
    {
        await _routeLock.WaitAsync();
        try
        {
            lock (_gate) if (version != _gameVersion || !_started) return;
        JsonDocument doc; try { doc = JsonDocument.Parse(line); } catch { await HostAsync(new { type = "server_notice", message = line }); return; }
        using (doc)
        {
            var root = doc.RootElement.Clone(); var target = root.GetProperty("message_type").GetString() ?? "internal"; var api = root.GetProperty("api").GetString() ?? ""; if (_exportingLog && api != "cli_log") return; if (api == "cli_log") _exportingLog = false;
            if (api == "request_player_list") return;
            var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload = root });
            if (api == "cli_log")
            {
                var content = root.TryGetProperty("data", out var logData) && logData.ValueKind == JsonValueKind.String ? logData.GetString() ?? "" : "";
                var download = JsonSerializer.SerializeToElement(new { type = "game_log_available", fileName = $"WereMF_{DateTime.Now:yyMMdd_HHmmss}_{_code}.log", content });
                Add(_publicHistory, download);
                await BroadcastAsync(download);
                return;
            }
            if (api == "request_for_next_game")
            {
                _exportingLog = true;
                await _game!.SendAsync("0");
                return;
            }
            if (api == "player_anonymous_init") { await ApplyAnonymousMappingAsync(root); return; }
            if (api == "pending_skill_created") { await RoutePendingSkillAsync(root); return; }
            if (api is "request_reroll_player" or "request_vote") { await RouteConcurrentRequestAsync(root, api); return; }
            if (api is "game_update_night" or "game_update_day") UpdateAliveCount(root);
            if (api is "vote_end_broadcast" or "day_start_broadcast" or "night_start_broadcast")
            {
                lock (_gate) { _concurrentInput = null; _expected = null; CancelTimerLocked(); }
            }
            var regularRequest = api.StartsWith("request_") && !api.EndsWith("_parse_error") && api != "request_player_list";
            if (regularRequest)
            {
                lock (_gate) { _concurrentInput = null; _expected = target; CancelTimerLocked(); }
            }
            if (target == "public" && api is "game_update_night" or "game_update_day" or "cli_game_summary")
            {
                PlayerSession[] recipients; lock (_gate) recipients = _players.Where(x => x.Connected).ToArray();
                foreach (var player in recipients)
                {
                    var redacted = RedactedEnvelope(root, player.GameId); Add(player.History, redacted); await SendAsync(player, redacted);
                }
                return;
            }
            if (target == "public") { Add(_publicHistory, envelope); await BroadcastAsync(envelope); }
            else if (target.StartsWith("player_") && int.TryParse(target[7..], out var id)) { var p = _players.FirstOrDefault(x => x.GameId == id); if (p is not null) { Add(p.History, envelope); await SendAsync(p, envelope); } }
            else { Add(_hostHistory, envelope); await HostAsync(envelope); }
            if (regularRequest)
            {
                var requestPlayer = PlayerForTarget(target);
                if (requestPlayer?.IsBot == true) await ResolveBotRequestAsync(root, api, target, requestPlayer);
                else await StartRegularTimerAsync(root, api, target);
            }
        }
        }
        finally { _routeLock.Release(); }
    }
    private async Task RoutePendingSkillAsync(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("source_player_id", out var source) || !source.TryGetInt32(out var id)) return;
        var player = _players.FirstOrDefault(x => x.GameId == id);
        if (player is null) return;
        var payload = JsonNode.Parse(root.GetRawText())!.AsObject();
        payload["message_type"] = $"player_{id}";
        var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload });
        Add(player.History, envelope);
        await SendAsync(player, envelope);
    }

    private async Task RouteConcurrentRequestAsync(JsonElement root, string api)
    {
        List<(PlayerSession Player, int Remaining)> prompts = [];
        string? cliInput;
        ConcurrentInputPhase phase;
        var startTimer = false;
        lock (_gate)
        {
            if (_concurrentInput is null || _concurrentInput.Api != api)
            {
                CancelTimerLocked();
                _concurrentInput = ConcurrentInputPhase.Create(api, root);
                phase = _concurrentInput;
                var seconds = api == "request_vote"
                    ? Math.Max(_aliveCount, phase.Remaining.Count) * _options.VoteSecondsPerAlive
                    : _options.RequestTimeoutSeconds;
                phase.Deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
                phase.TimerStarted = true;
                startTimer = true;
                prompts.AddRange(_players.Where(x => !x.IsBot && phase.Remaining.ContainsKey(x.GameId)).Select(x => (x, phase.Remaining[x.GameId])));
                ResolveConcurrentBotsLocked(phase);
            }
            else phase = _concurrentInput;
            phase.Prompt = root;
            phase.RefreshVoteRules(root);
            phase.CliWaiting = true;
            cliInput = TakeConcurrentCliInputLocked(phase);
        }
        foreach (var (player, remaining) in prompts) await SendConcurrentPromptAsync(player, root, remaining);
        if (startTimer) await ScheduleConcurrentTimerAsync(phase);
        if (cliInput is not null && _game is not null) await _game.SendAsync(cliInput);
    }

    private Task SendConcurrentPromptAsync(PlayerSession player, JsonElement root, int remaining, CancellationToken ct = default)
    {
        var payload = JsonNode.Parse(root.GetRawText())!.AsObject();
        payload["message_type"] = $"player_{player.GameId}";
        payload["web_concurrent"] = true;
        payload["web_remaining"] = remaining;
        payload["message_content"] = payload["api"]?.GetValue<string>() == "request_reroll_player"
            ? "是否使用本局唯一一次重抽身份机会？"
            : $"请选择你的投票（还可提交 {remaining} 次，第二次用于确认或改票）";
        return SendAsync(player, new { type = "game_message", payload }, ct);
    }
    private async Task ApplyAnonymousMappingAsync(JsonElement root)
    {
        var payload = JsonNode.Parse(root.GetRawText())!.AsObject();
        if (payload["data"] is not JsonArray players) return;
        PlayerSession[] sessions; lock (_gate) sessions = _players.ToArray();
        foreach (var item in players.OfType<JsonObject>())
        {
            var id = item["id"]?.GetValue<int>() ?? 0;
            var name = item["name"]?.GetValue<string>() ?? "";
            var session = sessions.FirstOrDefault(x => x.Name == name);
            if (session is null) continue;
            session.GameId = id;
            item["name"] = $"玩家{id}";
        }
        payload["message_type"] = "public";
        var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload });
        Add(_publicHistory, envelope);
        await BroadcastAsync(envelope);
        await Task.WhenAll(sessions.Select(x => SendAsync(x, new { type = "player_remapped", playerId = x.GameId })));
    }

    private void UpdateAliveCount(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
        var alive = 0;
        foreach (var entity in data.EnumerateArray())
        {
            if (!entity.TryGetProperty("state", out var state)) continue;
            var dead = state.TryGetProperty("is_dead_public", out var publicDead) && publicDead.ValueKind == JsonValueKind.True;
            if (!dead) alive++;
        }
        if (alive > 0) lock (_gate) _aliveCount = alive;
    }
    private static JsonElement RedactedEnvelope(JsonElement root, int playerId)
    {
        var payload = JsonNode.Parse(root.GetRawText())!.AsObject();
        var entities = payload["data"] switch
        {
            JsonArray direct => direct,
            JsonObject dataObject when dataObject["entities"] is JsonArray nested => nested,
            _ => null
        };
        if (entities is not null)
        {
            foreach (var item in entities.OfType<JsonObject>())
            {
                var id = item["player"]?["id"]?.GetValue<int>() ?? 0;
                if (item["player"] is JsonObject player && player["anonymous"]?.GetValue<bool>() == true)
                    player["name"] = $"玩家{id}";
                if (id != playerId) item["role"] = null;
            }
        }
        return JsonSerializer.SerializeToElement(new { type = "game_message", payload });
    }

    private static void Add(List<JsonElement> list, JsonElement value) { lock (list) { list.Add(value); if (list.Count > 250) list.RemoveAt(0); } }
    private async Task ReplayAsync(PlayerSession p, CancellationToken ct) { foreach (var x in _publicHistory.Concat(p.History).Concat(p.IsHost ? _hostHistory : [])) await SendAsync(p, x, ct); }
    private Task WelcomeAsync(PlayerSession p, CancellationToken ct) => SendAsync(p, new { type = "welcome", roomCode = _code, playerId = p.GameId, playerName = p.Name, token = p.Token, isHost = p.IsHost }, ct);
    private async Task StateAsync(CancellationToken ct = default)
    {
        PlayerSession[] snapshot; lock (_gate) snapshot = _players.ToArray();
        await BroadcastAsync(new { type = "room_state", roomCode = _code, started = _started, bots = snapshot.Where(p => p.IsBot).Select(p => p.GameId), players = snapshot.Select(p => new { id = p.Id, name = p.Name, connected = p.Connected, isHost = p.IsHost, isBot = p.IsBot, isPermanentBot = p.IsPermanentBot }) }, ct);
    }
    private Task HostAsync(object data) { var p = _players.FirstOrDefault(x => x.IsHost); return p is null ? Task.CompletedTask : SendAsync(p, data); }
    private async Task BroadcastAsync(object data, CancellationToken ct = default)
    {
        PlayerSession[] recipients; lock (_gate) recipients = _players.Where(x => x.Connected).ToArray();
        await Task.WhenAll(recipients.Select(x => SendAsync(x, data, ct)));
    }
    private async Task SendAsync(PlayerSession p, object data, CancellationToken ct = default)
    {
        if (!p.Connected || p.Socket is null || p.Socket.State != WebSocketState.Open) return;
        try { await p.SendLock.WaitAsync(ct); try { await GameServer.SendRawAsync(p.Socket, data, ct); } finally { p.SendLock.Release(); } } catch (WebSocketException) { p.Connected = false; }
    }
    public async Task DisconnectAsync(PlayerSession p) { p.Connected = false; await StateAsync(); }
    public async ValueTask DisposeAsync() { if (_game is not null) await _game.DisposeAsync(); await _remove(_code); }
}

internal sealed class ConcurrentInputPhase
{
    public required string Api { get; init; }
    public required JsonElement Prompt { get; set; }
    public Dictionary<int, int> Remaining { get; } = [];
    public Queue<(int PlayerId, string Value)> Queue { get; } = [];
    public Dictionary<int, HashSet<int>> InvalidVotes { get; } = [];
    public HashSet<int> CanSuicide { get; } = [];
    public HashSet<int> Responded { get; } = [];
    public bool CliWaiting { get; set; } = true;
    public DateTimeOffset Deadline { get; set; }
    public bool TimerStarted { get; set; }
    public bool TimedOut { get; set; }

    public static ConcurrentInputPhase Create(string api, JsonElement root)
    {
        var phase = new ConcurrentInputPhase { Api = api, Prompt = root };
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return phase;
        if (api == "request_reroll_player")
        {
            foreach (var item in data.EnumerateArray()) if (item.TryGetInt32(out var id)) phase.Remaining[id] = 1;
        }
        else
        {
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id)) continue;
                if (item.TryGetProperty("can_vote", out var canVote) && canVote.ValueKind == JsonValueKind.True) phase.Remaining[id] = 2;
            }
            phase.RefreshVoteRules(root);
        }
        return phase;
    }

    public void RefreshVoteRules(JsonElement root)
    {
        if (Api != "request_vote" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
        InvalidVotes.Clear(); CanSuicide.Clear();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id)) continue;
            if (item.TryGetProperty("can_suicide", out var suicide) && suicide.ValueKind == JsonValueKind.True) CanSuicide.Add(id);
            var invalid = new HashSet<int>();
            if (item.TryGetProperty("invalid_vote", out var invalidNode) && invalidNode.ValueKind == JsonValueKind.Array)
                foreach (var choice in invalidNode.EnumerateArray()) if (choice.TryGetProperty("id", out var choiceId) && choiceId.TryGetInt32(out var target)) invalid.Add(target);
            InvalidVotes[id] = invalid;
        }
    }
}
internal sealed class PlayerSession(int id, string name, bool host, WebSocket? socket)
{
    public int Id { get; set; } = id; public int GameId { get; set; } = id; public string Name { get; } = name; public bool IsHost { get; set; } = host; public WebSocket? Socket { get; set; } = socket; public bool Connected { get; set; } = true;
    public bool IsBot { get; set; } public bool IsPermanentBot { get; set; } public bool HasLeft { get; set; } public int MissedRequests { get; set; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); public SemaphoreSlim SendLock { get; } = new(1, 1); public List<JsonElement> History { get; } = []; public Dictionary<string, string> Drafts { get; } = [];
}
internal sealed record ClientMessage { public string Type { get; init; } = ""; public string? RoomCode { get; init; } public string? PlayerName { get; init; } public string? Token { get; init; } public string? Value { get; init; } public string? SkillId { get; init; } public string? Api { get; init; } }
internal sealed class ClientVisibleException(string message) : Exception(message);
