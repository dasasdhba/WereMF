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
    private GameProcess? _game; private bool _started; private string? _expected;
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
            p = token is null ? null! : _players.FirstOrDefault(x => x.Token == token)!;
            if (p is not null) { p.Socket = socket; p.Connected = true; }
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
        if (msg.Type == "game_input") { await InputAsync(p, msg.Value ?? "", ct); return; }
        if (msg.Type == "command")
        {
            if (!p.IsHost || _game is null) throw new ClientVisibleException("只有房主可以使用管理命令");
            var ok = new[] { "\\undo", "\\redo", "\\restart", "\\night", "\\day", "\\vote", "\\summary", "\\log null" };
            if (!ok.Contains(msg.Value)) throw new ClientVisibleException("不支持的命令"); await _game.SendAsync(msg.Value!, ct); return;
        }
        if (msg.Type == "ping") { await SendAsync(p, new { type = "pong" }, ct); return; }
        throw new ClientVisibleException("未知操作");
    }

    private async Task StartAsync(CancellationToken ct)
    {
        PlayerSession[] players;
        lock (_gate) { if (_started) throw new ClientVisibleException("对局已经开始"); if (_players.Count < 7) throw new ClientVisibleException("至少需要 7 名玩家"); _started = true; players = _players.ToArray(); }
        _game = new GameProcess(_options.GamePath, _options.Config, _options.Seed);
        _game.OutputReceived += RouteAsync; _game.Exited += code => BroadcastAsync(new { type = "game_ended", message = code == 0 ? "对局已结束" : "规则进程意外退出" }); _game.Start();
        await StateAsync(ct); await _game.SendAsync(string.Join(" ", players.Select(x => $"\"{x.Name.Replace("\"", "”")}\"")), ct);
    }
    private async Task InputAsync(PlayerSession p, string value, CancellationToken ct)
    {
        if (_game is null) throw new ClientVisibleException("对局尚未开始");
        var allowed = _expected == $"player_{p.Id}" || _expected == "public" || (_expected == "internal" && p.IsHost);
        if (!allowed) throw new ClientVisibleException("现在还没轮到你行动");
        _expected = null; await _game.SendAsync(value.Trim(), ct);
    }

    private async Task RouteAsync(string line)
    {
        JsonDocument doc; try { doc = JsonDocument.Parse(line); } catch { await HostAsync(new { type = "server_notice", message = line }); return; }
        using (doc)
        {
            var root = doc.RootElement.Clone(); var target = root.GetProperty("message_type").GetString() ?? "internal"; var api = root.GetProperty("api").GetString() ?? "";
            var envelope = JsonSerializer.SerializeToElement(new { type = "game_message", payload = root });
            if (api.StartsWith("request_") && !api.EndsWith("_parse_error")) _expected = target;
            if (target == "public" && api is "game_update_night" or "game_update_day" or "cli_game_summary")
            {
                PlayerSession[] recipients; lock (_gate) recipients = _players.Where(x => x.Connected).ToArray();
                foreach (var player in recipients)
                {
                    var redacted = RedactedEnvelope(root, player.Id); Add(player.History, redacted); await SendAsync(player, redacted);
                }
                return;
            }
            if (target == "public") { Add(_publicHistory, envelope); await BroadcastAsync(envelope); }
            else if (target.StartsWith("player_") && int.TryParse(target[7..], out var id)) { var p = _players.FirstOrDefault(x => x.Id == id); if (p is not null) { Add(p.History, envelope); await SendAsync(p, envelope); } }
            else { Add(_hostHistory, envelope); await HostAsync(envelope); }
        }
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
                if (id != playerId) item["role"] = null;
            }
        }
        return JsonSerializer.SerializeToElement(new { type = "game_message", payload });
    }

    private static void Add(List<JsonElement> list, JsonElement value) { lock (list) { list.Add(value); if (list.Count > 250) list.RemoveAt(0); } }
    private async Task ReplayAsync(PlayerSession p, CancellationToken ct) { foreach (var x in _publicHistory.Concat(p.History).Concat(p.IsHost ? _hostHistory : [])) await SendAsync(p, x, ct); }
    private Task WelcomeAsync(PlayerSession p, CancellationToken ct) => SendAsync(p, new { type = "welcome", roomCode = _code, playerId = p.Id, playerName = p.Name, token = p.Token, isHost = p.IsHost }, ct);
    private async Task StateAsync(CancellationToken ct = default)
    {
        PlayerSession[] snapshot; lock (_gate) snapshot = _players.ToArray();
        await BroadcastAsync(new { type = "room_state", roomCode = _code, started = _started, players = snapshot.Select(p => new { id = p.Id, name = p.Name, connected = p.Connected, isHost = p.IsHost }) }, ct);
    }
    private Task HostAsync(object data) { var p = _players.FirstOrDefault(x => x.IsHost); return p is null ? Task.CompletedTask : SendAsync(p, data); }
    private async Task BroadcastAsync(object data, CancellationToken ct = default)
    {
        PlayerSession[] recipients; lock (_gate) recipients = _players.Where(x => x.Connected).ToArray();
        await Task.WhenAll(recipients.Select(x => SendAsync(x, data, ct)));
    }
    private async Task SendAsync(PlayerSession p, object data, CancellationToken ct = default)
    {
        if (!p.Connected || p.Socket.State != WebSocketState.Open) return;
        try { await p.SendLock.WaitAsync(ct); try { await GameServer.SendRawAsync(p.Socket, data, ct); } finally { p.SendLock.Release(); } } catch (WebSocketException) { p.Connected = false; }
    }
    public async Task DisconnectAsync(PlayerSession p) { p.Connected = false; await StateAsync(); }
    public async ValueTask DisposeAsync() { if (_game is not null) await _game.DisposeAsync(); await _remove(_code); }
}

internal sealed class PlayerSession(int id, string name, bool host, WebSocket socket)
{
    public int Id { get; } = id; public string Name { get; } = name; public bool IsHost { get; } = host; public WebSocket Socket { get; set; } = socket; public bool Connected { get; set; } = true;
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); public SemaphoreSlim SendLock { get; } = new(1, 1); public List<JsonElement> History { get; } = [];
}
internal sealed record ClientMessage { public string Type { get; init; } = ""; public string? RoomCode { get; init; } public string? PlayerName { get; init; } public string? Token { get; init; } public string? Value { get; init; } }
internal sealed class ClientVisibleException(string message) : Exception(message);
