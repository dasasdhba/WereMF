using System.Text.Json;

namespace WereMFServer;

internal sealed class RoomHistory
{
    public const int ReconnectHistoryLimit = 250;
    public const int BotTimelineLimit = 4_000;
    public const int CliLogLimit = 100_000;
    public const int TrimCount = 1_000;
    public List<JsonElement> Public { get; } = [];
    public List<JsonElement> Host { get; } = [];
    public List<string> RawCliLog { get; } = [];
    public string? CompletedCliLog { get; set; }
    public Dictionary<int, List<string>> DayInteractions { get; } = [];
    public List<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> BotTimeline { get; } = [];
    public long BotTimelineSequence { get; set; }

    public void ResetForGame()
    {
        Public.Clear();
        Host.Clear();
        RawCliLog.Clear();
        CompletedCliLog = null;
        DayInteractions.Clear();
        BotTimeline.Clear();
        BotTimelineSequence = 0;
    }

    public void RecordDayInteraction(int day, string text)
    {
        if (day <= 0) return;
        if (!DayInteractions.TryGetValue(day, out var interactions)) DayInteractions[day] = interactions = [];
        interactions.Add(text);
    }

    public void AddRawCliLog(string line)
    {
        RawCliLog.Add(line);
        if (RawCliLog.Count > CliLogLimit) RawCliLog.RemoveRange(0, 10_000);
    }

    public void Append(List<JsonElement> history, JsonElement value, bool visibleToBots, int? recipientPlayerId)
    {
        if (visibleToBots)
        {
            BotTimeline.Add((++BotTimelineSequence, recipientPlayerId, value));
            if (BotTimeline.Count > BotTimelineLimit) BotTimeline.RemoveRange(0, TrimCount);
        }
        lock (history)
        {
            history.Add(value);
            if (history.Count > ReconnectHistoryLimit) history.RemoveAt(0);
        }
    }

    public string BuildDownloadLog(string cliLog) => GameLogAssembler.Merge(cliLog, DayInteractions);
}
