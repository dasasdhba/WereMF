using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace WereMFServer;

internal sealed class PlayerSession(int id, string name, bool host, WebSocket? socket)
{
    public int Id { get; set; } = id;
    public int GameId { get; set; } = id;
    public string Name { get; } = name;
    public bool IsHost { get; set; } = host;
    public WebSocket? Socket { get; set; } = socket;
    public bool Connected { get; set; } = true;
    public bool IsBot { get; set; }
    public bool IsPermanentBot { get; set; }
    public bool HasLeft { get; set; }
    public int MissedRequests { get; set; }
    public DateTimeOffset LastChatAt { get; set; } = DateTimeOffset.MinValue;
    public int BotConversationFailures { get; set; }
    public int BotDefensePending;
    public List<BotMemoryEntry> BotMemory { get; } = [];
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public List<JsonElement> History { get; } = [];
    public Dictionary<string, DraftEntry> Drafts { get; } = [];
}

internal sealed record ClientMessage
{
    public string Type { get; init; } = "";
    public string? RoomCode { get; init; }
    public string? PlayerName { get; init; }
    public string? Token { get; init; }
    public string? Value { get; init; }
    public string? SkillId { get; init; }
    public string? Api { get; init; }
    public bool? PreSubmit { get; init; }
    public int? RequestTimeoutSeconds { get; init; }
    public int? VoteSecondsPerAlive { get; init; }
    public int? VotePenaltySeconds { get; init; }
    public int? EventIntervalSeconds { get; init; }
}

internal sealed record RoomLogSnapshot(string FileName, string Content, bool Complete);
internal sealed record BotConcurrentDecision(PlayerSession Player, string Choice, int Remaining);
internal sealed record DraftEntry(string Value, bool PreSubmit);
internal sealed record RoomSettings(int RequestTimeoutSeconds, int VoteSecondsPerAlive, int VotePenaltySeconds, int EventIntervalSeconds);
internal sealed record BotMemoryEntry(string Text, string Kind, IReadOnlyList<long> SourceEvents, DateTimeOffset CreatedAt);
internal sealed record BotMemoryCandidate(string Text, string Kind, IReadOnlyList<long> SourceEvents);
internal sealed record BotModelDecision(string? Input, BotMemoryCandidate? Memory);
internal sealed class ClientVisibleException(string message) : Exception(message);
