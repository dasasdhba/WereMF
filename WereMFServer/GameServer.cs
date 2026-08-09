using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WereMFServer;

internal sealed class GameServer : IDisposable
{
    internal static readonly string[] RoleNames = ["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"];
    private readonly ServerOptions _options;
    private readonly LlmBotClient? _llmBot;
    private readonly string[] _botNames;
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    public int ActiveRoomCount => _rooms.Count;
    public bool LlmBotsEnabled => _llmBot is not null;
    public string? LlmBotModel => _llmBot is null ? null : _options.LlmApiKey is not null ? _options.LlmModel : _options.LlmFallbackModel;
    public object? LlmBotStats => _llmBot?.Stats;
    public GameServer(ServerOptions options)
    {
        _options = options;
        _botNames = LoadBotNames();
        LlmBotClient? fallback = null;
        if (!string.IsNullOrWhiteSpace(options.LlmFallbackEndpoint))
            fallback = new LlmBotClient(options.LlmFallbackEndpoint, options.LlmFallbackApiKey, options.LlmFallbackModel, options.LlmFallbackTimeoutSeconds, options.LlmFallbackMaxTokens, circuitFailureThreshold: 6, circuitBreakSeconds: 15);
        _llmBot = options.LlmApiKey is not null
            ? new LlmBotClient(options.LlmEndpoint, options.LlmApiKey, options.LlmModel, options.LlmTimeoutSeconds, options.LlmMaxTokens, fallback)
            : fallback;
    }
    private static string[] LoadBotNames()
    {
        var files = new[] { "bots_prefer.txt", "bots.txt" };
        return files.SelectMany(file =>
            File.Exists(Path.Combine(AppContext.BaseDirectory, file))
                ? File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, file))
                : [])
            .Select(name => name.Trim())
            .Where(name => name.Length is >= 1 and <= 20 && !name.StartsWith('#') && !RoleNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public object GetPublicRooms() => _rooms.Values.Where(x => x.IsJoinable).Select(x => x.PublicState()).ToArray();
    public RoomLogSnapshot? GetRoomLog(string code) => _rooms.TryGetValue(code, out var room) ? room.GetLog() : null;

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
        finally { if (room is not null && player is not null) await room.DisconnectAsync(player, socket); }
    }

    private async Task<(GameRoom, PlayerSession)> AttachAsync(WebSocket socket, ClientMessage msg, CancellationToken ct)
    {
        var name = (msg.PlayerName ?? "").Trim();
        if (name.Length is < 1 or > 20) throw new ClientVisibleException("昵称需要 1–20 个字符");
        if (RoleNames.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ClientVisibleException("昵称不能与身份名相同");
        if (msg.Type == "create_room")
        {
            var room = CreateRoom(); return (room, await room.CreateHostAsync(socket, name, ct));
        }
        if (msg.Type is "join_room" or "reconnect")
        {
            var code = (msg.RoomCode ?? "").Trim();
            if (!_rooms.TryGetValue(code, out var room)) throw new ClientVisibleException("房间不存在或已经结束");
            return (room, await room.JoinAsync(socket, name, msg.Token, msg.Type == "reconnect", ct));
        }
        throw new ClientVisibleException("请先创建或加入房间");
    }

    private GameRoom CreateRoom()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            var room = new GameRoom(code, _options, _llmBot, _botNames, c => { _rooms.TryRemove(c, out _); return Task.CompletedTask; });
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
    public void Dispose() { foreach (var room in _rooms.Values) room.DisposeAsync().AsTask().GetAwaiter().GetResult(); _llmBot?.Dispose(); }
}

internal sealed class GameRoom : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _code; private readonly ServerOptions _options; private readonly Func<string, Task> _remove;
    private readonly LlmBotClient? _llmBot;
    private readonly string[] _botNames;
    private RoomSettings _settings;
    private readonly List<PlayerSession> _players = [];
    private readonly RoomHistory _history = new();
    private List<JsonElement> _publicHistory => _history.Public;
    private List<JsonElement> _hostHistory => _history.Host;
    private List<string> _rawCliLog => _history.RawCliLog;
    private string? _completedCliLog { get => _history.CompletedCliLog; set => _history.CompletedCliLog = value; }
    private Dictionary<int, List<string>> _dayInteractions => _history.DayInteractions;
    private readonly Dictionary<string, string> _pendingSkillRoles = new(StringComparer.Ordinal);
    private List<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> _botTimeline => _history.BotTimeline;
    private long _botTimelineSequence { get => _history.BotTimelineSequence; set => _history.BotTimelineSequence = value; }
    private GameProcess? _game; private bool _started; private bool _finished; private bool _exportingLog;
    private readonly PendingDraftStore _pendingDrafts = new();
    private readonly RegularInputCoordinator _regularInput = new();
    private readonly ConcurrentInputCoordinator _concurrentInputs = new();
    private readonly BotVisibleContextBuilder _botVisibleContext = new();
    private readonly BotCoordinator _botCoordinator;
    private int? _lastCliInputPlayerId; private string? _lastCliInputApi;
    private string? _expected { get => _regularInput.ExpectedTarget; set { if (value is null) _regularInput.Clear(); else _regularInput.SetExpectedTarget(value); } }
    private JsonElement? _regularPrompt { get => _regularInput.Prompt; set => _regularInput.SetPrompt(value); }
    private string? _regularApi { get => _regularInput.Api; set => _regularInput.SetApi(value); }
    private DateTimeOffset _regularDeadline { get => _regularInput.Deadline; set => _regularInput.SetDeadline(value); }
    private string? _presentationEndApi; private DateTimeOffset _presentationNextAt; private bool _presentationClosing;
    private ConcurrentInputPhase? _concurrentInput { get => _concurrentInputs.Current; set => _concurrentInputs.Current = value; }
    private bool _dayChatOpen; private HashSet<int> _chatEligible = [];
    private int _botDayGeneration;
    private int _dayStateGeneration;
    private CancellationTokenSource? _botChatCts;
    private Task _botOpeningTask = Task.CompletedTask;
    private int _botReplyPending;
    private DateTimeOffset _lastBotChatAt;
    private DateTimeOffset _lastHumanChatAt;
    private long _botReplyVersion;
    private int _botFollowupsRemaining;
    private static readonly TimeSpan BotOpeningDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan BotReplyDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BotMessageInterval = TimeSpan.FromMilliseconds(1500);
    private string _gameModeSummary = "模式尚未公布";
    private readonly SemaphoreSlim _routeLock = new(1, 1);
    private CancellationTokenSource? _timerCts; private long _timerVersion; private long _gameVersion;
    public bool IsJoinable => !_started && !_finished && _players.Count < 16;
    public GameRoom(string code, ServerOptions options, LlmBotClient? llmBot, string[] botNames, Func<string, Task> remove)
    {
        (_code, _options, _llmBot, _botNames, _remove) = (code, options, llmBot, botNames, remove);
        _botCoordinator = new(llmBot);
        _settings = new RoomSettings(options.RequestTimeoutSeconds, options.VoteSecondsPerAlive, options.VotePenaltySeconds, options.EventIntervalSeconds);
    }
    public object PublicState() => new { code = _code, players = _players.Count, maxPlayers = 16, started = _started };
    public RoomLogSnapshot GetLog()
    {
        lock (_gate)
        {
            var content = BuildDownloadLogLocked(_completedCliLog ?? string.Join('\n', _rawCliLog));
            return new RoomLogSnapshot($"WereMF_{DateTime.Now:yyMMdd_HHmmss}_{_code}{(_completedCliLog is null ? "_running" : "")}.log", content, _completedCliLog is not null);
        }
    }
    private string BuildDownloadLogLocked(string cliLog)
        => _history.BuildDownloadLog(cliLog);

    private void RecordDayInteractionLocked(string text)
    {
        _history.RecordDayInteraction(_botDayGeneration, text);
    }

    private void RecordVoteInteractionLocked(PlayerSession player, string vote, bool confirmation)
    {
        var prefix = confirmation ? "确认投票给" : "投票给";
        var text = vote switch
        {
            "0" => $"{player.Name} {(confirmation ? "确认弃票" : "弃票")}",
            "b" => $"{player.Name} 自爆",
            _ => $"{player.Name} {prefix} {_players.FirstOrDefault(x => x.GameId.ToString() == vote)?.Name ?? $"{vote} 号"}"
        };
        if (_concurrentInput?.Api == "request_vote") _concurrentInput.LastPublicActivityAt = DateTimeOffset.UtcNow;
        RecordDayInteractionLocked(text);
    }

    public async Task<PlayerSession> CreateHostAsync(WebSocket socket, string name, CancellationToken ct)
    {
        var p = new PlayerSession(1, name, true, socket); lock (_gate) _players.Add(p); await WelcomeAsync(p, ct); await StateAsync(ct); return p;
    }
    public async Task<PlayerSession> JoinAsync(WebSocket socket, string name, string? token, bool reconnect, CancellationToken ct)
    {
        PlayerSession p;
        lock (_gate)
        {
            p = token is null ? null! : _players.FirstOrDefault(x => x.Token == token && !x.HasLeft)!;
            if (p is null && reconnect) throw new ClientVisibleException("会话已失效，请重新加入房间");
            if (p is not null) { p.Socket = socket; p.Connected = true; if (!p.IsPermanentBot) { p.IsBot = false; p.MissedRequests = 0; } }
            else
            {
                if (_started) throw new ClientVisibleException("对局已经开始，请使用原设备重连");
                if (_players.Count >= 16) throw new ClientVisibleException("房间已满");
                name = UniquePlayerNameLocked(name);
                p = new PlayerSession(_players.Count + 1, name, false, socket); _players.Add(p);
            }
        }
        await WelcomeAsync(p, ct); await ReplayAsync(p, ct); await StateAsync(ct); return p;
    }

    private string UniquePlayerNameLocked(string requestedName)
    {
        bool Available(string candidate) => !_players.Any(x => x.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (Available(requestedName)) return requestedName;

        // Keep the final nickname within the public 20-character limit.
        var prefix = requestedName[..Math.Min(requestedName.Length, 15)];
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = $"{prefix} {RandomNumberGenerator.GetInt32(10_000):D4}";
            if (Available(candidate)) return candidate;
        }

        // Retain a deterministic fallback in the extremely unlikely event of repeated collisions.
        for (var suffix = 0; suffix < 100_000; suffix++)
        {
            var digits = suffix.ToString();
            var deterministicPrefix = requestedName[..Math.Min(requestedName.Length, 19 - digits.Length)];
            var candidate = $"{deterministicPrefix} {digits}";
            if (Available(candidate)) return candidate;
        }
        throw new ClientVisibleException("无法分配唯一昵称，请更换昵称后重试");
    }

    public async Task HandleAsync(PlayerSession p, ClientMessage msg, CancellationToken ct)
    {
        if (msg.Type == "start_game") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以开始"); await StartAsync(ct); return; }
        if (msg.Type == "add_bot") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以增加 Bot"); await AddBotAsync(ct); return; }
        if (msg.Type == "remove_bot") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以删除 Bot"); await RemoveBotAsync(ct); return; }
        if (msg.Type == "restart_room") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以重开"); await RestartRoomAsync(ct); return; }
        if (msg.Type == "update_room_settings") { if (!p.IsHost) throw new ClientVisibleException("只有房主可以修改房间设置"); await UpdateSettingsAsync(msg, ct); return; }
        if (msg.Type == "game_input") { await InputAsync(p, msg.Value ?? "", ct); return; }
        if (msg.Type == "pending_draft") { SaveDraft(p, msg); return; }
        if (msg.Type == "chat") { await ChatAsync(p, msg.Value ?? "", ct); return; }
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
            if (_started && !_finished)
            {
                player.HasLeft = true;
                player.IsBot = true;
                player.MissedRequests = 0;
                becameBot = true;
            }
            else
            {
                _players.Remove(player);
                if (!_started)
                    for (var i = 0; i < _players.Count; i++) _players[i].Id = _players[i].GameId = i + 1;
            }
            if (player.IsHost)
            {
                player.IsHost = false;
                var candidates = _players.Where(x => x != player && x.Connected && !x.IsBot && !x.HasLeft).ToArray();
                if (candidates.Length > 0) candidates[Random.Shared.Next(candidates.Length)].IsHost = true;
                else if (!_started || _finished) removeRoom = true;
            }
            remaining = _players.Where(x => x.Connected && !x.IsPermanentBot && !x.HasLeft).ToArray();
            if (_finished && remaining.Length == 0) removeRoom = true;
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
                _presentationEndApi = null; _presentationClosing = false; _presentationNextAt = default;
                game = _game; _game = null; _started = false; _finished = false; _exportingLog = false; _expected = null; _concurrentInput = null; _dayChatOpen = false; _chatEligible.Clear();
                CancelBotChatLocked();
                CancelTimerLocked();
                _publicHistory.Clear(); _hostHistory.Clear();
                _gameModeSummary = "模式尚未公布";
                _players.RemoveAll(x => x.HasLeft);
                for (var i = 0; i < _players.Count; i++)
                {
                    var player = _players[i];
                    player.Id = player.GameId = i + 1; player.MissedRequests = 0; player.History.Clear(); player.Drafts.Clear(); player.BotMemorySummary = ""; player.BotMemoryDay = -1;
                    if (!player.IsPermanentBot) player.IsBot = false;
                }
            }
        }
        finally { _routeLock.Release(); }
        await game.DisposeAsync();
        await BroadcastAsync(new { type = "room_restarted", message = "房主已结束本局，返回等待大厅" }, ct);
        PlayerSession[] sessions; lock (_gate) sessions = _players.Where(x => x.Connected && !x.IsPermanentBot).ToArray();
        await Task.WhenAll(sessions.Select(x => SendAsync(x, new { type = "session_state", playerId = x.GameId, isHost = x.IsHost }, ct)));
        await StateAsync(ct);
    }

    private async Task AddBotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局开始后不能增加 Bot");
            if (_players.Count >= 16) throw new ClientVisibleException("房间已满");
            var botName = _botNames.FirstOrDefault(name => !_players.Any(player => player.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            if (botName is null)
            {
                var number = 1;
                while (_players.Any(x => x.Name.Equals($"Bot {number}", StringComparison.OrdinalIgnoreCase))) number++;
                botName = $"Bot {number}";
            }
            _players.Add(new PlayerSession(_players.Count + 1, botName, false, null) { IsBot = true, IsPermanentBot = true });
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

    private async Task UpdateSettingsAsync(ClientMessage msg, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局开始后不能修改房间设置");
            _settings = new RoomSettings(
                SettingValue(msg.RequestTimeoutSeconds, _settings.RequestTimeoutSeconds, 5, 600, "普通请求限时"),
                SettingValue(msg.VoteSecondsPerAlive, _settings.VoteSecondsPerAlive, 5, 600, "每名存活玩家投票时长"),
                SettingValue(msg.VotePenaltySeconds, _settings.VotePenaltySeconds, 0, 600, "每次投票扣减时长"),
                SettingValue(msg.EventIntervalSeconds, _settings.EventIntervalSeconds, 0, 10, "消息展示间隔"));
        }
        await StateAsync(ct);
    }

    private static int SettingValue(int? value, int current, int min, int max, string name)
    {
        if (value is null) return current;
        if (value < min || value > max) throw new ClientVisibleException($"{name}需要在 {min}–{max} 秒之间");
        return value.Value;
    }
    private void SaveDraft(PlayerSession player, ClientMessage msg)
    {
        var value = (msg.Value ?? "").Trim();
        _pendingDrafts.Save(player, msg.SkillId, msg.Api, value, msg.PreSubmit == true);
    }
    private async Task ChatAsync(PlayerSession player, string value, CancellationToken ct)
    {
        var text = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 300) throw new ClientVisibleException("聊天消息需要 1–300 个字符");
        JsonElement envelope; long replyVersion;
        lock (_gate)
        {
            if (!_started || _finished || !_dayChatOpen) throw new ClientVisibleException("当前不是白天，不能发言");
            if (player.IsBot || player.HasLeft) throw new ClientVisibleException("Bot 托管席位不能发言");
            if (!_chatEligible.Contains(player.GameId)) throw new ClientVisibleException("已出局玩家不能发言");
            var now = DateTimeOffset.UtcNow;
            if (now - player.LastChatAt < TimeSpan.FromMilliseconds(500)) throw new ClientVisibleException("发言太快了，请稍后再试");
            player.LastChatAt = now;
            envelope = JsonSerializer.SerializeToElement(new { type = "chat_message", playerId = player.GameId, text, sentAt = now.ToUnixTimeMilliseconds() });
            Add(_publicHistory, envelope);
            RecordDayInteractionLocked($"{player.Name}: {text}");
            _lastHumanChatAt = now;
            if (_concurrentInput?.Api == "request_vote") _concurrentInput.LastPublicActivityAt = now;
            replyVersion = ++_botReplyVersion;
        }
        await BroadcastAsync(envelope, ct);
        if (_llmBot is not null) _ = RunBotRepliesAfterDelayAsync($"{player.GameId} 号玩家“{player.Name}”说：{text}", replyVersion);
    }

    private void UpdateChatPermission(JsonElement root, string api)
    {
        lock (_gate)
        {
            if (api == "day_start_broadcast")
            {
                CancelBotChatLocked();
                _dayChatOpen = true;
                _botDayGeneration++;
                _botFollowupsRemaining = 3;
                _botChatCts = new CancellationTokenSource();
                return;
            }
            if (api is "night_start_broadcast" or "game_win_broadcast")
            {
                _dayChatOpen = false; _chatEligible.Clear();
                CancelBotChatLocked();
                return;
            }
            if (api != "game_update_day") return;
            var entities = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data : default;
            var eligible = new HashSet<int>();
            if (entities.ValueKind == JsonValueKind.Array)
                foreach (var entity in entities.EnumerateArray())
                {
                    if (!entity.TryGetProperty("player", out var playerNode) || !playerNode.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id)) continue;
                    if (!entity.TryGetProperty("state", out var stateNode)) continue;
                    var dead = stateNode.TryGetProperty("is_dead", out var deadNode) && deadNode.ValueKind == JsonValueKind.True;
                    if (!dead) eligible.Add(id);
                }
            _chatEligible = eligible;
            _dayStateGeneration++;
        }
    }
    private void CancelBotChatLocked()
    {
        _botChatCts?.Cancel();
        _botChatCts?.Dispose();
        _botChatCts = null;
        _botOpeningTask = Task.CompletedTask;
        Interlocked.Exchange(ref _botReplyPending, 0);
        _botReplyVersion++;
        _botFollowupsRemaining = 0;
        _lastHumanChatAt = default;
    }

    private void StartBotDayOpening()
    {
        if (_llmBot is null) return;
        lock (_gate)
        {
            if (!_started || _finished || !_dayChatOpen || _botChatCts is null) return;
            var day = _botDayGeneration;
            var stateGeneration = _dayStateGeneration;
            var token = _botChatCts.Token;
            _botOpeningTask = RunBotOpeningAsync(day, stateGeneration, token);
        }
    }

    private async Task RunBotOpeningAsync(int day, int stateGeneration, CancellationToken ct)
    {
        try
        {
            var openingScheduledAt = DateTimeOffset.UtcNow;
            for (var i = 0; i < 20; i++)
            {
                lock (_gate) if (!_dayChatOpen || day != _botDayGeneration) return;
                if (_dayStateGeneration != stateGeneration) break;
                await Task.Delay(100, ct);
            }
            await Task.Delay(BotOpeningDelay, ct);
            lock (_gate)
            {
                if (!_dayChatOpen || day != _botDayGeneration || _lastHumanChatAt >= openingScheduledAt || _concurrentInput?.Api == "request_vote") return;
            }

            PlayerSession[] bots;
            lock (_gate)
                bots = _players.Where(x => x.IsBot && !x.HasLeft && _chatEligible.Contains(x.GameId)).OrderBy(_ => Random.Shared.Next()).ToArray();
            var speeches = await Task.WhenAll(bots.Select(async bot => (Bot: bot, Decision: await DecideBotConversationAsync(bot, "白天刚开始；请自行判断现在是否值得首先发言", null, ct))));
            lock (_gate)
            {
                if (!_dayChatOpen || day != _botDayGeneration || _concurrentInput?.Api == "request_vote") return;
            }
            foreach (var speech in speeches.Where(x => !string.IsNullOrWhiteSpace(x.Decision?.Text)))
            {
                if (!await BroadcastBotChatAsync(speech.Bot, speech.Decision!.Text, day, ct)) return;
                await Task.Delay(BotMessageInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunBotRepliesAfterDelayAsync(string trigger, long version, int? excludedBotId = null, bool conversationOnly = false)
    {
        try
        {
            CancellationToken ct;
            lock (_gate)
            {
                if (!_started || _finished || !_dayChatOpen || _botChatCts is null) return;
                ct = _botChatCts.Token;
            }
            await Task.Delay(BotReplyDelay, ct);
            lock (_gate) if (version != _botReplyVersion || !_dayChatOpen) return;
            await RunBotRepliesAsync(trigger, excludedBotId, conversationOnly);
        }
        catch (OperationCanceledException) { }
    }

    private void ScheduleBotFollowup(PlayerSession speaker, string text)
    {
        long version;
        lock (_gate)
        {
            if (_botFollowupsRemaining <= 0 || !_dayChatOpen || _botChatCts is null) return;
            _botFollowupsRemaining--;
            version = ++_botReplyVersion;
        }
        _ = RunBotRepliesAfterDelayAsync(
            $"{speaker.GameId} 号 Bot“{speaker.Name}”说：{text}",
            version,
            speaker.GameId,
            conversationOnly: true);
    }

    private async Task ApplySilentBotVoteAfterDelayAsync(PlayerSession player, string vote, ConcurrentInputPhase phase, CancellationToken ct)
    {
        var delay = RandomNumberGenerator.GetInt32(800, 4_501);
        await Task.Delay(delay, ct);
        await ApplyBotConversationVoteAsync(player, vote, phase, ct);
    }

    private async Task RunBotRepliesAsync(string trigger, int? excludedBotId = null, bool conversationOnly = false)
    {
        if (_llmBot is null || Interlocked.CompareExchange(ref _botReplyPending, 1, 0) != 0) return;
        try
        {
            CancellationToken ct; int day; ConcurrentInputPhase? votePhase;
            lock (_gate)
            {
                if (!_started || _finished || !_dayChatOpen || _botChatCts is null) return;
                ct = _botChatCts.Token;
                day = _botDayGeneration;
                votePhase = conversationOnly ? null : _concurrentInput?.Api == "request_vote" ? _concurrentInput : null;
            }
            var cooldown = BotMessageInterval - (DateTimeOffset.UtcNow - _lastBotChatAt);
            if (cooldown > TimeSpan.Zero) await Task.Delay(cooldown, ct);
            PlayerSession[] bots;
            lock (_gate)
            {
                var candidates = _players.Where(x => x.IsBot && !x.HasLeft && _chatEligible.Contains(x.GameId) && x.GameId != excludedBotId)
                    .OrderByDescending(x => trigger.Contains(x.Name, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(_ => Random.Shared.Next());
                if (conversationOnly)
                {
                    var mentioned = candidates.Where(x => trigger.Contains(x.Name, StringComparison.OrdinalIgnoreCase)).Take(1).ToArray();
                    bots = mentioned.Length > 0 ? mentioned : candidates.Take(3).ToArray();
                }
                else bots = candidates.ToArray();
            }

            var pending = bots.Select(async bot => (Bot: bot, Decision: await DecideBotConversationAsync(bot, trigger, votePhase, ct))).ToList();
            var delayedSilentVotes = new List<Task>();
            var spoke = false;
            var speechCount = 0;
            var speechLimit = conversationOnly ? 1 : 2;
            PlayerSession? followupSpeaker = null;
            string? followupText = null;
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var turn = await completed;
                if (turn.Decision is null) continue;

                var hasSpeech = speechCount < speechLimit && !string.IsNullOrWhiteSpace(turn.Decision.Text);
                if (hasSpeech)
                {
                    if (!await BroadcastBotChatAsync(turn.Bot, turn.Decision.Text, day, ct)) return;
                    spoke = true;
                    speechCount++;
                    followupSpeaker = turn.Bot;
                    followupText = turn.Decision.Text;
                    if (votePhase is not null && !string.IsNullOrWhiteSpace(turn.Decision.Vote))
                        await ApplyBotConversationVoteAsync(turn.Bot, turn.Decision.Vote, votePhase, ct);
                    if (pending.Count > 0) await Task.Delay(BotMessageInterval, ct);
                }
                else if (votePhase is not null && !string.IsNullOrWhiteSpace(turn.Decision.Vote))
                {
                    delayedSilentVotes.Add(ApplySilentBotVoteAfterDelayAsync(turn.Bot, turn.Decision.Vote, votePhase, ct));
                }
            }

            if (!spoke && !conversationOnly && trigger.StartsWith("投票刚开始", StringComparison.Ordinal) && bots.Length > 0)
            {
                var initiator = bots[RandomNumberGenerator.GetInt32(bots.Length)];
                var interaction = await DecideBotConversationAsync(
                    initiator,
                    "全场第一轮都保持沉默；你是本轮唯一开场者。请使用 information_probe，基于当前权威状态或公开票型向一名存活玩家提出一个具体短问题，不得 silent，不要暴露无必要的精确身份",
                    null,
                    ct);
                if (!string.IsNullOrWhiteSpace(interaction?.Text) &&
                    await BroadcastBotChatAsync(initiator, interaction.Text, day, ct))
                {
                    followupSpeaker = initiator;
                    followupText = interaction.Text;
                }
            }

            if (delayedSilentVotes.Count > 0) await Task.WhenAll(delayedSilentVotes);
            if (followupSpeaker is not null && !string.IsNullOrWhiteSpace(followupText))
                ScheduleBotFollowup(followupSpeaker, followupText);
        }
        catch (OperationCanceledException) { }
        finally { Interlocked.Exchange(ref _botReplyPending, 0); }
    }
    private async Task<BotConversationDecision?> DecideBotConversationAsync(PlayerSession player, string trigger, ConcurrentInputPhase? votePhase, CancellationToken ct)
    {
        if (_llmBot is null) return null;
        string voteContext;
        lock (_gate) voteContext = BuildBotVoteContextLocked(player, votePhase);
        var visibleContext = await BuildBotVisibleContextAsync(player, ct);
        var ruleFocus = BuildBotRuleFocus(player, votePhase?.Api ?? "speech");
        var context = new BotSpeechContext(player.GameId, player.Name, trigger, visibleContext, voteContext, ruleFocus);
        var decision = await _llmBot.SpeakAsync(context, ct);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var requiredVoteChoices = Array.Empty<string>();
            if (votePhase is not null && _concurrentInput == votePhase && votePhase.Api == "request_vote" &&
                votePhase.Remaining.TryGetValue(player.GameId, out var remaining) && remaining > 0 &&
                (remaining == 1 ||
                 votePhase.Deadline - now <= TimeSpan.FromSeconds(60) ||
                 remaining == 2 && now - votePhase.LastPublicActivityAt >= TimeSpan.FromSeconds(_options.LlmBotThinkSeconds)))
            {
                requiredVoteChoices = LegalBotVoteChoicesLocked(player, votePhase)
                    .Where(x => int.TryParse(x, out var id) && id > 0)
                    .ToArray();
            }
            if (decision is not null)
            {
                player.BotConversationFailures = 0;
                if (requiredVoteChoices.Length > 0 && !requiredVoteChoices.Contains(decision.Vote))
                    return decision with { Vote = requiredVoteChoices[RandomNumberGenerator.GetInt32(requiredVoteChoices.Length)] };
                return decision;
            }
            if (!_started || _finished || !_dayChatOpen || !player.IsBot || player.HasLeft) return null;
            player.BotConversationFailures++;
            if (votePhase is not null && _concurrentInput == votePhase && votePhase.Api == "request_vote" &&
                votePhase.Remaining.GetValueOrDefault(player.GameId) > 0 && player.BotConversationFailures >= 2)
            {
                var choices = LegalBotVoteChoicesLocked(player, votePhase)
                    .Where(x => int.TryParse(x, out var id) && id > 0)
                    .ToArray();
                var vote = choices.Length > 0 ? choices[RandomNumberGenerator.GetInt32(choices.Length)] : "0";
                player.BotConversationFailures = 0;
                return new BotConversationDecision("", vote);
            }
            if (player.BotConversationFailures >= 3 && trigger.Contains(player.Name, StringComparison.OrdinalIgnoreCase))
            {
                player.BotConversationFailures = 0;
                return new BotConversationDecision("我先保留判断。", null);
            }
            return null;
        }
    }

    private string BuildBotVoteContextLocked(PlayerSession player, ConcurrentInputPhase? phase)
    {
        if (phase is null || _concurrentInput != phase || phase.Api != "request_vote")
            return "投票尚未开始：vote 必须为 null。";
        var remaining = phase.Remaining.GetValueOrDefault(player.GameId);
        var choices = LegalBotVoteChoicesLocked(player, phase);
        var now = DateTimeOffset.UtcNow;
        var voteElapsed = Math.Max(0, (int)(now - phase.StartedAt).TotalSeconds);
        var publicQuietSeconds = Math.Max(0, (int)(now - phase.LastPublicActivityAt).TotalSeconds);
        var budget = Math.Max(0, (int)(phase.Deadline - now).TotalSeconds);
        var latest = phase.LatestVotes.TryGetValue(player.GameId, out var previous) ? previous : "尚未投票";
        return $"投票阶段实际经过 {voteElapsed} 秒；距离场上最近一次发言或投票已有 {publicQuietSeconds} 秒；投票初始预算 {phase.InitialSeconds} 秒；当前投票预算剩余 {budget} 秒（每张有效票另扣 {_settings.VotePenaltySeconds} 秒）。你还可投 {remaining} 次；当前票：{latest}；合法 vote：{string.Join('、', choices)}。有合法非零目标时默认现在投票；仅在第一次机会、预算超过 60 秒且场上尚未持续沉默时可返回 null。第一票尚未使用且场上连续沉默达到一次思考间隔、预算不超过 60 秒或只剩最后一次机会时，不得继续观望。";
    }

    private List<string> LegalBotVoteChoicesLocked(PlayerSession player, ConcurrentInputPhase phase)
    {
        var choices = Enumerable.Range(0, _players.Count + 1)
            .Except(phase.InvalidVotes.TryGetValue(player.GameId, out var invalid) ? invalid : [])
            .Select(x => x.ToString()).ToList();
        if (phase.CanSuicide.Contains(player.GameId)) choices.Add("b");
        return choices;
    }

    private async Task ApplyBotConversationVoteAsync(PlayerSession player, string? vote, ConcurrentInputPhase phase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vote)) return;
        string? cliInput = null; var reschedule = false;
        lock (_gate)
        {
            if (_concurrentInput != phase || phase.Api != "request_vote" || !player.IsBot || !phase.Remaining.TryGetValue(player.GameId, out var remaining) || remaining <= 0) return;
            if (!LegalBotVoteChoicesLocked(player, phase).Contains(vote)) return;
            phase.Queue.Enqueue((player.GameId, $"{player.GameId} {vote}"));
            phase.Responded.Add(player.GameId);
            phase.LatestVotes[player.GameId] = vote;
            RecordVoteInteractionLocked(player, vote, remaining < 2);
            phase.Remaining[player.GameId] = vote == "b" ? 0 : remaining - 1;
            if (!phase.TimedOut)
            {
                phase.Deadline = phase.Deadline.AddSeconds(-_settings.VotePenaltySeconds);
                reschedule = phase.Remaining.Values.Any(x => x > 0);
            }
            cliInput = TakeConcurrentCliInputLocked(phase);
        }
        if (reschedule) await ScheduleConcurrentTimerAsync(phase);
        if (cliInput is not null) await SendCliInputAsync(cliInput, ct);
    }

    private void StartBotVoteThinkLoop(ConcurrentInputPhase phase)
    {
        CancellationToken ct;
        lock (_gate)
        {
            if (_concurrentInput != phase || phase.BotThinkLoopStarted || _botChatCts is null) return;
            phase.BotThinkLoopStarted = true;
            ct = _botChatCts.Token;
        }
        _ = RunBotVoteThinkLoopAsync(phase, ct);
    }

    private async Task RunBotVoteThinkLoopAsync(ConcurrentInputPhase phase, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.LlmBotThinkSeconds), ct);
                int voteElapsed; int budget;
                lock (_gate)
                {
                    if (_concurrentInput != phase || phase.Api != "request_vote" || !_dayChatOpen || phase.TimedOut) return;
                    var now = DateTimeOffset.UtcNow;
                    voteElapsed = Math.Max(0, (int)(now - phase.StartedAt).TotalSeconds);
                    budget = Math.Max(0, (int)(phase.Deadline - now).TotalSeconds);
                    if (budget <= 0) return;
                }
                await RunBotRepliesAsync($"定时思考：投票阶段开始至今 {voteElapsed} 秒，当前投票预算剩余 {budget} 秒；重新判断是否发言、是否投票或改票");
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> BroadcastBotChatAsync(PlayerSession player, string text, int day, CancellationToken ct)
    {
        JsonElement envelope;
        lock (_gate)
        {
            if (!_started || _finished || !_dayChatOpen || day != _botDayGeneration || !player.IsBot || player.HasLeft || !_chatEligible.Contains(player.GameId)) return false;
            var now = DateTimeOffset.UtcNow;
            _lastBotChatAt = now;
            if (_concurrentInput?.Api == "request_vote") _concurrentInput.LastPublicActivityAt = now;
            envelope = JsonSerializer.SerializeToElement(new { type = "chat_message", playerId = player.GameId, text, sentAt = now.ToUnixTimeMilliseconds(), bot = true });
            Add(_publicHistory, envelope);
            RecordDayInteractionLocked($"{player.Name}: {text}");
        }
        await BroadcastAsync(envelope, ct);
        return true;
    }

    private async Task StartAsync(CancellationToken ct)
    {
        PlayerSession[] players; GameProcess game; long version;
        lock (_gate)
        {
            if (_started) throw new ClientVisibleException("对局已经开始");
            if (_players.Count < 7) throw new ClientVisibleException("至少需要 7 名玩家");
            CancelBotChatLocked();
            _started = true; _finished = false; _dayChatOpen = false; _chatEligible.Clear(); players = _players.ToArray(); version = ++_gameVersion;
            _presentationEndApi = null; _presentationClosing = false; _presentationNextAt = default;
            _rawCliLog.Clear(); _completedCliLog = null; _dayInteractions.Clear();
            _botTimeline.Clear(); _botTimelineSequence = 0; _pendingSkillRoles.Clear();
            foreach (var player in players) { player.BotMemorySummary = ""; player.BotMemoryDay = -1; player.BotConversationFailures = 0; }
            _botDayGeneration = 0; _dayStateGeneration = 0; _lastBotChatAt = default;
            _gameModeSummary = "模式尚未公布";
            _game = game = new GameProcess(_options.GamePath, _options.Config, _options.Seed);
        }
        game.OutputReceived += line => RouteAsync(line, version);
        game.Exited += code => OnGameExitedAsync(code, version);
        game.Start();
        await StateAsync(ct);
        await SendCliInputAsync(string.Join(" ", players.Select(x => $"\"{x.Name.Replace("\"", "”")}\"")), ct);
    }
    private async Task OnGameExitedAsync(int code, long version)
    {
        lock (_gate)
        {
            if (version != _gameVersion || !_started) return;
            _finished = true;
            CancelBotChatLocked();
            _dayChatOpen = false; _chatEligible.Clear();
            _expected = null;
            _concurrentInput = null;
            CancelTimerLocked();
        }
        await BroadcastAsync(new { type = "game_ended", message = code == 0 ? "对局已结束" : "规则进程意外退出" });
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
        string api;
        lock (_gate)
        {
            var allowed = _expected == $"player_{p.GameId}" || _expected == "public" || (_expected == "internal" && p.IsHost);
            if (!allowed) throw new ClientVisibleException("现在还没轮到你行动");
            p.MissedRequests = 0;
            api = _regularApi ?? "";
            _expected = null;
            _regularPrompt = null;
            _regularApi = null;
            CancelTimerLocked();
        }
        await RecordCliInputAsync(p, api, value, ct);
        await SendCliInputAsync(value, ct);
    }

    private async Task ConcurrentInputAsync(PlayerSession player, string value, CancellationToken ct)
    {
        string? cliInput;
        JsonElement? repeatPrompt = null;
        ConcurrentInputPhase? reschedule = null;
        int remaining;
        string api;
        string recordedInput;
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
                recordedInput = trimmed == "1" ? player.GameId.ToString() : "0";
                if (trimmed == "1") phase.Queue.Enqueue((player.GameId, recordedInput));
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
                    recordedInput = $"{player.GameId} b";
                    phase.Queue.Enqueue((player.GameId, recordedInput));
                }
                else
                {
                    if (!int.TryParse(trimmed, out var target) || target < 0 || target > _players.Count)
                        throw new ClientVisibleException("投票目标无效");
                    if (phase.InvalidVotes.TryGetValue(player.GameId, out var invalid) && invalid.Contains(target))
                        throw new ClientVisibleException("当前不能投给这名玩家");
                    recordedInput = $"{player.GameId} {target}";
                    phase.Queue.Enqueue((player.GameId, recordedInput));
                }
            }
            player.MissedRequests = 0;
            phase.Responded.Add(player.GameId);
            if (phase.Api == "request_vote")
            {
                phase.LatestVotes[player.GameId] = trimmed;
                RecordVoteInteractionLocked(player, trimmed, remaining < 2);
            }
            remaining = --phase.Remaining[player.GameId];
            if (remaining > 0) repeatPrompt = phase.Prompt;
            if (phase.Api == "request_vote" && !phase.TimedOut)
            {
                phase.Deadline = phase.Deadline.AddSeconds(-_settings.VotePenaltySeconds);
                reschedule = phase;
            }
            cliInput = TakeConcurrentCliInputLocked(phase);
        }
        await SendAsync(player, new { type = "input_accepted", api, remaining }, ct);
        await RecordCliInputAsync(player, api, recordedInput, ct);
        if (repeatPrompt is JsonElement prompt) await SendConcurrentPromptAsync(player, prompt, remaining, ct);
        if (reschedule is not null) await ScheduleConcurrentTimerAsync(reschedule);
        if (cliInput is not null) await SendCliInputAsync(cliInput, ct);
    }

    private string? TakeConcurrentCliInputLocked(ConcurrentInputPhase phase)
    {
        if (!phase.CliWaiting) return null;
        if (phase.Queue.TryDequeue(out var queued))
        {
            phase.CliWaiting = false;
            _lastCliInputPlayerId = queued.PlayerId;
            _lastCliInputApi = phase.Api;
            return queued.Value;
        }
        if (phase.Remaining.Values.All(x => x == 0) &&
            (phase.Api == "request_reroll_player" || phase.TimedOut))
        {
            phase.CliWaiting = false;
            _lastCliInputPlayerId = null;
            _lastCliInputApi = phase.Api;
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
        var fallback = RandomLegalInput(root, api);
        var value = await DecideBotInputAsync(player, root, api, fallback);
        SetCliInputOrigin(player, api);
        await SendCliInputAsync(value);
    }

    private async Task<string> DecideBotInputAsync(PlayerSession player, JsonElement root, string api, string fallback)
    {
        return await _botCoordinator.DecideInputAsync(
            player,
            root,
            api,
            fallback,
            () => BuildBotVisibleContextAsync(player),
            () => BuildBotRuleFocus(player, api, root),
            (request, requestApi, candidate) => IsLegalTimeoutInput(request, requestApi, candidate));
    }

    private string BuildBotRuleFocus(PlayerSession player, string api, JsonElement? request = null)
    {
        var roles = new List<string>();
        JsonElement currentState;
        string? pendingRole = null;
        lock (_gate)
        {
            currentState = _botTimeline
                .Where(x => x.RecipientPlayerId is null || x.RecipientPlayerId == player.GameId)
                .Select(x => x.Envelope)
                .LastOrDefault(CliMessageRouter.IsAuthoritativeState);
            if (request is JsonElement requestRoot && ExtractSkillId(requestRoot) is string skillId)
                _pendingSkillRoles.TryGetValue(skillId, out pendingRole);
        }

        if (!string.IsNullOrWhiteSpace(pendingRole)) roles.Add(pendingRole);
        if (currentState.ValueKind != JsonValueKind.Undefined &&
            currentState.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("data", out var data))
        {
            var entities = data.ValueKind == JsonValueKind.Array
                ? data
                : data.ValueKind == JsonValueKind.Object && data.TryGetProperty("entities", out var nested)
                    ? nested
                    : default;
            if (entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    if (!entity.TryGetProperty("player", out var playerNode) ||
                        !playerNode.TryGetProperty("id", out var idNode) ||
                        !idNode.TryGetInt32(out var id) || id != player.GameId ||
                        !entity.TryGetProperty("role", out var roleNode))
                        continue;
                    BotVisibleContextBuilder.CollectRoleTypes(roleNode, roles);
                    break;
                }
            }
        }
        return BotGameKnowledge.Focus(api, roles);
    }

    private string BuildBotVisibleContext(PlayerSession player, int take = 24)
    {
        (long Sequence, int? RecipientPlayerId, JsonElement Envelope)[] timeline;
        string mode;
        lock (_gate)
        {
            timeline = _botTimeline
                .Where(x => x.RecipientPlayerId is null || x.RecipientPlayerId == player.GameId)
                .ToArray();
            mode = _gameModeSummary;
        }

        return _botVisibleContext.Build(timeline, player.GameId, mode, take);
    }

    private async Task<string> BuildBotVisibleContextAsync(PlayerSession player, CancellationToken ct = default)
    {
        if (_llmBot is null) return BuildBotVisibleContext(player);
        var raw = BuildBotVisibleContext(player);
        bool hasOlderEvents;
        lock (_gate)
            hasOlderEvents = _botTimeline.Count(x => x.RecipientPlayerId is null || x.RecipientPlayerId == player.GameId) > 24;
        int day; long version;
        lock (_gate) { day = _botDayGeneration; version = _gameVersion; }

        if ((hasOlderEvents || raw.Length > 18_000) && player.BotMemoryDay != day)
        {
            await player.BotMemoryLock.WaitAsync(ct);
            try
            {
                if (player.BotMemoryDay != day)
                {
                    player.BotMemoryDay = day;
                    var summary = await _llmBot.SummarizeAsync(player.GameId, player.Name, player.BotMemorySummary, raw, ct);
                    lock (_gate)
                        if (version == _gameVersion && summary is not null) player.BotMemorySummary = summary;
                }
            }
            finally { player.BotMemoryLock.Release(); }
        }

        var memory = player.BotMemorySummary;
        return string.IsNullOrWhiteSpace(memory)
            ? raw
            : $"【长期记忆（仅历史稳定事实，不代表临时效果仍存在）】\n{memory}\n\n【当前局面与近期事件】\n{BuildBotVisibleContext(player, 10)}";
    }


    private async Task ResolveConcurrentBotsAsync(ConcurrentInputPhase phase, JsonElement root, PlayerSession[] bots)
    {
        var decisions = await Task.WhenAll(bots.Select(async player =>
        {
            var remaining = phase.Remaining[player.GameId];
            if (phase.Api == "request_reroll_player")
            {
                var fallback = RandomNumberGenerator.GetInt32(2).ToString();
                if (_llmBot is null) return new BotConcurrentDecision(player, fallback, remaining);
                var rerollContext = await BuildBotVisibleContextAsync(player);
                var context = new BotDecisionContext(player.GameId, player.Name, phase.Api, root.GetRawText(), rerollContext, "只能是 1（重抽）或 0（保留）", BuildBotRuleFocus(player, phase.Api, root));
                var candidate = await _llmBot.DecideAsync(context);
                var rerollAccepted = candidate is "0" or "1";
                _llmBot.ReportValidation(rerollAccepted);
                return new BotConcurrentDecision(player, rerollAccepted ? candidate! : fallback, remaining);
            }

            var choices = Enumerable.Range(0, _players.Count + 1)
                .Except(phase.InvalidVotes.TryGetValue(player.GameId, out var invalid) ? invalid : [])
                .Select(x => x.ToString()).ToList();
            if (phase.CanSuicide.Contains(player.GameId)) choices.Add("b");
            var fallbackVote = choices[RandomNumberGenerator.GetInt32(choices.Count)];
            if (_llmBot is null) return new BotConcurrentDecision(player, fallbackVote, remaining);
            var visibleContext = await BuildBotVisibleContextAsync(player);
            var voteContext = new BotDecisionContext(player.GameId, player.Name, phase.Api, root.GetRawText(), visibleContext, $"只输出投票目标编号（合法集合：{string.Join('、', choices)}）；b 表示脚滑人自爆，0 表示弃票");
            var vote = await _llmBot.DecideAsync(voteContext);
            var voteAccepted = vote is not null && choices.Contains(vote);
            _llmBot.ReportValidation(voteAccepted);
            return new BotConcurrentDecision(player, voteAccepted ? vote! : fallbackVote, remaining);
        }));

        lock (_gate)
        {
            if (_concurrentInput != phase) return;
            foreach (var decision in decisions)
            {
                var playerId = decision.Player.GameId;
                if (!phase.Remaining.TryGetValue(playerId, out var current) || current <= 0) continue;
                if (phase.Api == "request_reroll_player")
                {
                    if (decision.Choice == "1") phase.Queue.Enqueue((playerId, playerId.ToString()));
                    phase.Remaining[playerId] = 0;
                    continue;
                }
                var submissions = decision.Choice == "b" ? 1 : decision.Remaining;
                for (var i = 0; i < submissions; i++)
                {
                    phase.Queue.Enqueue((playerId, $"{playerId} {decision.Choice}"));
                    if (phase.Api == "request_vote" && !phase.TimedOut)
                        phase.Deadline = phase.Deadline.AddSeconds(-_settings.VotePenaltySeconds);
                }
                phase.Remaining[playerId] = 0;
            }
        }
    }

    private async Task RecordCliInputAsync(PlayerSession player, string api, string value, CancellationToken ct = default)
    {
        SetCliInputOrigin(player, api);
        var envelope = JsonSerializer.SerializeToElement(new
        {
            type = "cli_input_recorded",
            api,
            value,
            sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        Add(player.History, envelope);
        await SendAsync(player, envelope, ct);
    }

    private void SetCliInputOrigin(PlayerSession? player, string api)
    {
        lock (_gate)
        {
            _lastCliInputPlayerId = player?.GameId;
            _lastCliInputApi = api;
        }
    }

    private async Task SendCliInputAsync(string value, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _history.AddRawCliLog(JsonSerializer.Serialize(new { debug_direction = "input", value, at = DateTimeOffset.UtcNow }));
        }
        var game = _game;
        if (game is not null) await game.SendAsync(value, ct);
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
            deadline = DateTimeOffset.UtcNow.AddSeconds(_settings.RequestTimeoutSeconds);
            _regularDeadline = deadline;
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
        SetCliInputOrigin(player, api);
        await SendCliInputAsync(value);
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

        if (cliInput is not null) await SendCliInputAsync(cliInput);
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
        var draft = player is not null ? _pendingDrafts.Find(player, skillId ?? "", api) : null;
        if (draft is not null && !string.IsNullOrWhiteSpace(draft.Value) && IsLegalTimeoutInput(root, api, draft.Value)) return (draft.Value, "draft");
        return (RandomLegalInput(root, api), "random");
    }

    private (bool Armed, bool Valid, string Value, string? SkillId) TakePreSubmit(JsonElement root, string api, PlayerSession player)
    {
        var skillId = ExtractSkillId(root);
        var draft = _pendingDrafts.TakePreSubmit(player, skillId, api);
        if (draft is null) return (false, false, "", skillId);
        return (true, IsLegalTimeoutInput(root, api, draft.Value), draft.Value, skillId);
    }
    private async Task CancelThreatenedPreSubmitAsync(JsonElement root, string api, string target)
    {
        var player = PlayerForTarget(target);
        var skillId = ExtractSkillId(root);
        if (player is null || skillId is null) return;
        var preSubmitCanceled = false;
        preSubmitCanceled = _pendingDrafts.RemovePreSubmit(player, skillId);
        if (!preSubmitCanceled) return;
        var message = api == "myz_threaten_force_notify"
            ? "该技能受到 myz 强制威胁，原预选与预提交已清除；若角色仍有附加选项，请重新决定"
            : "你已被 myz 威胁，原预选与预提交已清除；请在轮到行动时重新决定，违抗威胁会在下一次夜晚开始时死亡";
        await SendAsync(player, new { type = "pre_submit_rejected", api, skillId, message });
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
        if (api == "request_hechong_copy_leaf") return parts.Length == 1 && GameServer.RoleNames.Contains(parts[0]) && parts[0] is not ("叶子" or "合虫");
        if (parts.Length == 1 && parts[0] == "0") return true;
        var ids = parts.Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
        var (minimum, maximum) = SelectionRange(root, api);
        if (ids.Length < minimum || ids.Length > maximum || (api != "request_myz_skill" && ids.Distinct().Count() != ids.Length)) return false;
        if (ids.Any(x => x <= 0 || x > _players.Count)) return false;
        if (api == "request_myz_skill")
        {
            if (InvalidChoices(root, "invalid_choice").Contains(ids[0])) return false;
            if (InvalidChoices(root, "invalid_target_choice").Contains(ids[1])) return false;
        }
        else if (ids.Any(InvalidChoices(root, "invalid_choice").Contains)) return false;
        if (api == "request_rabi_skill" && !parts.Any(x => x is "x" or "d")) return false;
        if (api == "request_jiaohua_dead_skill" && !parts.Any(x => x is "x" or "p")) return false;
        return true;
    }

    private static (int Minimum, int Maximum) SelectionRange(JsonElement root, string api)
    {
        if (api == "request_leaf_charas") return (4, 4);
        if (api == "request_myz_skill") return (2, 2);
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("choice_count", out var count) && count.TryGetInt32(out var exact)) return (exact, exact);
            var minimum = data.TryGetProperty("choice_min", out var min) && min.TryGetInt32(out var minValue) ? minValue : 1;
            if (data.TryGetProperty("choice_max", out var max) && max.TryGetInt32(out var maxValue)) return (minimum, maxValue);
        }
        var text = root.TryGetProperty("message_content", out var content) ? content.GetString() ?? "" : "";
        var match = System.Text.RegularExpressions.Regex.Match(text, @"最多\s*(\d+)\s*个");
        return match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? (1, parsed) : (1, 1);
    }
    private string RandomLegalInput(JsonElement root, string api)
    {
        if (api == "request_leaf_charas") return RandomLeafChoice(root);
        if (api == "request_xiansong_skill_force_threaten") return RandomChoice(["m", "x", "0"]);
        if (api.Contains("force_threaten") && api != "request_myz_skill_force_threaten") return RandomNumberGenerator.GetInt32(2).ToString();
        if (IsBooleanRequest(api)) return RandomNumberGenerator.GetInt32(2).ToString();
        if (api == "request_hechong_copy_leaf")
        {
            var text = root.TryGetProperty("message_content", out var content) ? content.GetString() ?? "" : "";
            var choices = System.Text.RegularExpressions.Regex.Matches(text, @"(?<!\d)(\d+)\s*[：:]")
                .Select(match => match.Groups[1].Value).Distinct().ToArray();
            return choices.Length > 0 ? RandomChoice(choices) : "1";
        }
        var valid = Enumerable.Range(1, _players.Count).Except(InvalidChoices(root, "invalid_choice")).ToArray();
        if (valid.Length == 0) return "0";
        var first = valid[RandomNumberGenerator.GetInt32(valid.Length)];
        if (api == "request_myz_skill")
        {
            var targets = Enumerable.Range(1, _players.Count).Except(InvalidChoices(root, "invalid_target_choice")).ToArray();
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
    private async Task PacePresentationAsync(JsonElement root, string api)
    {
        if (_presentationClosing)
        {
            var closingDelay = _presentationNextAt - DateTimeOffset.UtcNow;
            if (closingDelay > TimeSpan.Zero) await Task.Delay(closingDelay);
            _presentationEndApi = null;
            _presentationClosing = false;
            _presentationNextAt = default;
        }

        if (_presentationEndApi is null)
        {
            _presentationEndApi = api switch
            {
                "night_summary_broadcast" => "day_start_broadcast",
                "vote_end_broadcast" => "night_start_broadcast",
                _ => null
            };
            if (_presentationEndApi is null) return;
        }

        var delay = _presentationNextAt - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);

        var text = root.TryGetProperty("message_content", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? ""
            : "";
        var visible = !string.IsNullOrWhiteSpace(text) &&
                      !api.StartsWith("request_", StringComparison.Ordinal) &&
                      !api.StartsWith("game_update_", StringComparison.Ordinal);
        if (visible)
            _presentationNextAt = DateTimeOffset.UtcNow.AddSeconds(_settings.EventIntervalSeconds);

        if (api == _presentationEndApi || api == "game_win_broadcast")
            _presentationClosing = true;
    }
    private async Task RouteAsync(string line, long version)
    {
        await _routeLock.WaitAsync();
        try
        {
            lock (_gate) if (version != _gameVersion || !_started) return;
        if (!CliEnvelope.TryParse(line, out var cliEnvelope) || cliEnvelope is null)
        {
            await HostAsync(new { type = "server_notice", message = line });
            return;
        }
        var routeKind = CliMessageRouter.Classify(cliEnvelope);
        {
            var root = cliEnvelope.Root; var target = cliEnvelope.Target; var api = cliEnvelope.Api; if (_exportingLog && api != "cli_log") return; if (api == "cli_log") _exportingLog = false;
            lock (_gate) { if (api != "cli_log") _history.AddRawCliLog(line); }
            var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload = root });
            if (routeKind == CliRouteKind.Ignored) return;
            if (routeKind == CliRouteKind.ParseError)
            {
                PlayerSession? source;
                lock (_gate)
                {
                    source = _lastCliInputApi is not null && api.StartsWith(_lastCliInputApi, StringComparison.Ordinal) && _lastCliInputPlayerId is int sourceId ? _players.FirstOrDefault(x => x.GameId == sourceId) : null;
                    _lastCliInputPlayerId = null;
                    _lastCliInputApi = null;
                }
                if (source is not null && !source.IsBot)
                {
                    Add(source.History, envelope);
                    await SendAsync(source, envelope);
                }
                return;
            }
            if (api == "game_win_broadcast")
            {
                lock (_gate)
                {
                    _finished = true;
                    _dayChatOpen = false;
                    _chatEligible.Clear();
                    _expected = null;
                    _concurrentInput = null;
                    CancelBotChatLocked();
                    CancelTimerLocked();
                }
            }
            await PacePresentationAsync(root, api);
            UpdateChatPermission(root, api);
            if (api == "game_mode_broadcast")
            {
                var modeText = root.TryGetProperty("message_content", out var modeContent) ? modeContent.GetString() ?? "" : "";
                var modeData = root.TryGetProperty("data", out var structuredMode)
                    ? structuredMode.GetRawText()
                    : "{}";
                lock (_gate) _gameModeSummary = $"{modeText}\n结构化数据：{modeData}";
            }
            if (routeKind == CliRouteKind.Log)
            {
                lock (_gate) { _finished = true; _expected = null; _concurrentInput = null; CancelTimerLocked(); CancelBotChatLocked(); }
                var content = root.TryGetProperty("data", out var logData) && logData.ValueKind == JsonValueKind.String ? logData.GetString() ?? "" : "";
                lock (_gate) { _completedCliLog = content; content = BuildDownloadLogLocked(content); }
                var download = JsonSerializer.SerializeToElement(new { type = "game_log_available", fileName = $"WereMF_{DateTime.Now:yyMMdd_HHmmss}_{_code}.log", content });
                Add(_publicHistory, download);
                await BroadcastAsync(download);
                return;
            }
            if (routeKind == CliRouteKind.NextGame)
            {
                _exportingLog = true;
                await SendCliInputAsync("0");
                return;
            }
            if (routeKind == CliRouteKind.NightPatch)
            {
                if (target != "public" || !IsValidNightPatch(root))
                {
                    await HostAsync(new { type = "server_notice", message = "已忽略非法的灰卡比夜间公开增量" });
                    return;
                }
                Add(_publicHistory, envelope);
                await BroadcastAsync(envelope);
                return;
            }
            if (routeKind == CliRouteKind.AnonymousMapping) { await ApplyAnonymousMappingAsync(root); return; }
            if (routeKind == CliRouteKind.PendingSkill) { await RoutePendingSkillAsync(root); return; }
            if (api is "myz_threaten_notify" or "myz_threaten_force_notify")
                await CancelThreatenedPreSubmitAsync(root, api, target);
            if (routeKind == CliRouteKind.ConcurrentRequest) { await RouteConcurrentRequestAsync(root, api); return; }

            if (api is "vote_end_broadcast" or "day_start_broadcast" or "night_start_broadcast")
            {
                lock (_gate) { _concurrentInput = null; _expected = null; CancelTimerLocked(); }
            }
            var regularRequest = api.StartsWith("request_") && !api.EndsWith("_parse_error") && api != "request_player_list";
            if (regularRequest)
            {
                lock (_gate)
                {
                    _concurrentInput = null; _expected = target;
                    _regularPrompt = root; _regularApi = api;
                    CancelTimerLocked();
                }
                var requestPlayer = PlayerForTarget(target);
                if (requestPlayer is not null && !requestPlayer.IsBot)
                {
                    var preSubmit = TakePreSubmit(root, api, requestPlayer);
                    if (preSubmit.Armed && preSubmit.Valid)
                    {
                        lock (_gate) { _expected = null; requestPlayer.MissedRequests = 0; CancelTimerLocked(); }
                        await SendAsync(requestPlayer, new { type = "pre_submit_accepted", api, skillId = preSubmit.SkillId, value = preSubmit.Value, message = $"已按预提交自动行动：{preSubmit.Value}" });
                        await RecordCliInputAsync(requestPlayer, api, preSubmit.Value);
                        await SendCliInputAsync(preSubmit.Value);
                        return;
                    }
                    if (preSubmit.Armed)
                        await SendAsync(requestPlayer, new { type = "pre_submit_rejected", api, skillId = preSubmit.SkillId, message = "局面已变化，预提交不再合法，请重新确认" });
                }
            }
            if (routeKind == CliRouteKind.Snapshot)
            {
                PlayerSession[] recipients; lock (_gate) recipients = _players.Where(x => x.Connected).ToArray();
                foreach (var player in recipients)
                {
                    var redacted = CliRouteTransforms.RedactSnapshot(root, player.GameId); Add(player.History, redacted); await SendAsync(player, redacted);
                }
                return;
            }
            if (target == "public") { Add(_publicHistory, envelope); await BroadcastAsync(envelope); }
            else if (target.StartsWith("player_") && int.TryParse(target[7..], out var id)) { var p = _players.FirstOrDefault(x => x.GameId == id); if (p is not null) { Add(p.History, envelope); await SendAsync(p, envelope); } }
            else { Add(_hostHistory, envelope); await HostAsync(envelope); }
            if (api == "day_start_broadcast") StartBotDayOpening();
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
        if (data.TryGetProperty("id", out var skillIdNode) && skillIdNode.ValueKind == JsonValueKind.String &&
            data.TryGetProperty("type", out var roleTypeNode) && roleTypeNode.ValueKind == JsonValueKind.String)
        {
            var skillId = skillIdNode.GetString();
            var roleType = roleTypeNode.GetString();
            if (!string.IsNullOrWhiteSpace(skillId) && !string.IsNullOrWhiteSpace(roleType))
                lock (_gate) _pendingSkillRoles[skillId] = roleType;
        }
        var player = _players.FirstOrDefault(x => x.GameId == id);
        if (player is null) return;
        var payload = CliRouteTransforms.CreatePlayerTargetPayload(root, id);
        var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload });
        Add(player.History, envelope);
        await SendAsync(player, envelope);
    }

    private async Task RouteConcurrentRequestAsync(JsonElement root, string api)
    {
        List<(PlayerSession Player, int Remaining)> prompts = [];
        PlayerSession[] bots = [];
        string? cliInput = null;
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
                    ? phase.Remaining.Count * _settings.VoteSecondsPerAlive
                    : _settings.RequestTimeoutSeconds;
                phase.StartedAt = DateTimeOffset.UtcNow;
                phase.LastPublicActivityAt = phase.StartedAt;
                phase.InitialSeconds = seconds;
                phase.Deadline = phase.StartedAt.AddSeconds(seconds);
                phase.TimerStarted = true;
                startTimer = true;
                prompts.AddRange(_players.Where(x => !x.IsBot && phase.Remaining.ContainsKey(x.GameId)).Select(x => (x, phase.Remaining[x.GameId])));
                bots = _players.Where(x => x.IsBot && phase.Remaining.TryGetValue(x.GameId, out var count) && count > 0).ToArray();
            }
            else phase = _concurrentInput;
            phase.Prompt = root;
            phase.RefreshVoteRules(root);
            phase.CliWaiting = true;
            if (bots.Length == 0) cliInput = TakeConcurrentCliInputLocked(phase);
        }
        foreach (var (player, remaining) in prompts) await SendConcurrentPromptAsync(player, root, remaining);
        if (startTimer) await ScheduleConcurrentTimerAsync(phase);
        if (api == "request_vote" && startTimer && bots.Length > 0 && _llmBot is not null)
        {
            _ = RunBotRepliesAsync("投票刚开始；结合真实已过时间和当前投票预算，决定是否发言以及是否现在投第一票");
            StartBotVoteThinkLoop(phase);
            lock (_gate) cliInput = TakeConcurrentCliInputLocked(phase);
        }
        else if (bots.Length > 0)
        {
            await ResolveConcurrentBotsAsync(phase, root, bots);
            lock (_gate) cliInput = TakeConcurrentCliInputLocked(phase);
        }
        if (cliInput is not null) await SendCliInputAsync(cliInput);
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
        PlayerSession[] sessions; lock (_gate) sessions = _players.ToArray();
        var knownNames = sessions.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (!CliRouteTransforms.TryCreateAnonymousPayload(root, knownNames, out var payload, out var mappings)) return;
        foreach (var mapping in mappings)
        {
            var session = sessions.FirstOrDefault(x => x.Name == mapping.Name);
            if (session is not null) session.GameId = mapping.PlayerId;
        }
        await StateAsync();
        var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload });
        Add(_publicHistory, envelope);
        await BroadcastAsync(envelope);
        await Task.WhenAll(sessions.Select(x => SendAsync(x, new { type = "player_remapped", playerId = x.GameId })));
    }

    private bool IsValidNightPatch(JsonElement root)
    {
        return NightPatchValidator.IsValid(root, _players.Select(x => x.GameId).ToHashSet());
    }

    private void Add(List<JsonElement> list, JsonElement value)
    {
        bool visibleToBots;
        int? recipientPlayerId = null;
        lock (_gate)
        {
            visibleToBots = ReferenceEquals(list, _publicHistory);
            if (!visibleToBots)
            {
                var recipient = _players.FirstOrDefault(x => ReferenceEquals(x.History, list));
                if (recipient is not null)
                {
                    visibleToBots = true;
                    recipientPlayerId = recipient.GameId;
                }
            }
            _history.Append(list, value, visibleToBots, recipientPlayerId);
        }
    }
    private async Task ReplayAsync(PlayerSession p, CancellationToken ct)
    {
        var history = _publicHistory.Concat(p.History).Concat(p.IsHost ? _hostHistory : []).Where(x => !IsRequestEnvelope(x)).ToArray();
        foreach (var x in history) await SendAsync(p, x, ct);

        JsonElement? regularPrompt = null;
        string? regularApi = null;
        ConcurrentInputPhase? concurrent = null;
        int remaining = 0;
        DateTimeOffset deadline = default;
        lock (_gate)
        {
            if (!p.IsBot && _expected == $"player_{p.GameId}" && _regularPrompt is JsonElement prompt)
            {
                regularPrompt = prompt;
                regularApi = _regularApi;
                deadline = _regularDeadline;
            }
            else if (!p.IsBot && _concurrentInput is { TimedOut: false } phase &&
                     phase.Remaining.TryGetValue(p.GameId, out remaining) && remaining > 0)
            {
                concurrent = phase;
                deadline = phase.Deadline;
            }
        }
        if (regularPrompt is JsonElement currentRegular)
        {
            await SendAsync(p, new { type = "game_message", payload = currentRegular }, ct);
            await SendAsync(p, new { type = "request_timer", api = regularApi, deadlineUtc = deadline.ToUnixTimeMilliseconds(), mode = "request" }, ct);
        }
        else if (concurrent is not null)
        {
            await SendConcurrentPromptAsync(p, concurrent.Prompt, remaining, ct);
            await SendAsync(p, new { type = "request_timer", api = concurrent.Api, deadlineUtc = deadline.ToUnixTimeMilliseconds(), mode = concurrent.Api == "request_vote" ? "vote" : "request" }, ct);
        }
    }

    private static bool IsRequestEnvelope(JsonElement envelope)
    {
        return envelope.TryGetProperty("type", out var type) &&
               type.GetString() == "game_message" &&
               envelope.TryGetProperty("payload", out var payload) &&
               payload.TryGetProperty("api", out var api) &&
               (api.GetString() ?? "").StartsWith("request_", StringComparison.Ordinal);
    }
    private Task WelcomeAsync(PlayerSession p, CancellationToken ct) => SendAsync(p, new { type = "welcome", roomCode = _code, playerId = p.GameId, playerName = p.Name, token = p.Token, isHost = p.IsHost }, ct);
    private async Task StateAsync(CancellationToken ct = default)
    {
        PlayerSession[] snapshot; RoomSettings settings; lock (_gate) { snapshot = _players.ToArray(); settings = _settings; }
        await BroadcastAsync(new { type = "room_state", roomCode = _code, started = _started, settings, bots = snapshot.Where(p => p.IsBot).Select(p => p.GameId), players = snapshot.Select(p => new { id = p.Id, name = p.Name, connected = p.Connected, isHost = p.IsHost, isBot = p.IsBot, isPermanentBot = p.IsPermanentBot }) }, ct);
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
    public async Task DisconnectAsync(PlayerSession player, WebSocket socket)
    {
        var removeRoom = false;
        var removeSeat = false;
        var reindexSeats = false;
        PlayerSession[] remaining;
        lock (_gate)
        {
            if (!ReferenceEquals(player.Socket, socket)) return;
            player.Connected = false;
            player.Socket = null;
            removeSeat = !_started || _finished;
            reindexSeats = !_started;
            if (removeSeat)
            {
                _players.Remove(player);
                player.HasLeft = true;
                if (reindexSeats)
                    for (var i = 0; i < _players.Count; i++) _players[i].Id = _players[i].GameId = i + 1;
                if (player.IsHost)
                {
                    player.IsHost = false;
                    var candidates = _players.Where(x => x.Connected && !x.IsBot && !x.HasLeft).ToArray();
                    if (candidates.Length > 0) candidates[Random.Shared.Next(candidates.Length)].IsHost = true;
                    else removeRoom = true;
                }
            }
            remaining = _players.Where(x => x.Connected && !x.IsPermanentBot && !x.HasLeft).ToArray();
            if (_finished && remaining.Length == 0) removeRoom = true;
        }
        if (removeRoom) { await _remove(_code); return; }
        if (removeSeat)
            await Task.WhenAll(remaining.Select(x => SendAsync(x, new { type = "session_state", playerId = x.GameId, isHost = x.IsHost })));
        await StateAsync();
    }
    public async ValueTask DisposeAsync() { lock (_gate) CancelBotChatLocked(); if (_game is not null) await _game.DisposeAsync(); await _remove(_code); }
}

