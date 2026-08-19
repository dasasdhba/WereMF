using System.Text.Json;

namespace WereMFServer;

internal sealed class ConcurrentInputCoordinator
{
    public ConcurrentInputPhase? Current { get; set; }
    public bool IsActive => Current is not null;
    public void Clear() => Current = null;
}

internal sealed class ConcurrentInputPhase
{
    public required string Api { get; init; }
    public required JsonElement Prompt { get; set; }
    public Dictionary<int, int> Remaining { get; } = [];
    public Queue<(int PlayerId, string Value)> Queue { get; } = [];
    public Dictionary<int, HashSet<int>> InvalidVotes { get; } = [];
    public HashSet<int> CanSuicide { get; } = [];
    public Dictionary<int, string> LatestVotes { get; } = [];
    public HashSet<int> Responded { get; } = [];
    public HashSet<int> DefenseTriggered { get; } = [];
    public bool CliWaiting { get; set; } = true;
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastPublicActivityAt { get; set; }
    public int InitialSeconds { get; set; }
    public bool TimerStarted { get; set; }
    public bool TimedOut { get; set; }
    public bool BotThinkLoopStarted { get; set; }

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
        InvalidVotes.Clear();
        CanSuicide.Clear();
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
