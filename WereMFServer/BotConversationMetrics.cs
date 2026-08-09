using System.Threading;

namespace WereMFServer;

// Counts orchestration outcomes, rather than individual model attempts.  This is
// intentionally process-wide so /api/health does not expose room or player data.
internal sealed class BotConversationMetrics
{
    private long _triggers;
    private long _chatBroadcasts;
    private long _allSilentTriggers;
    private long _staleSpeechDiscards;
    private long _stateChangeRetries;

    public void RecordTrigger() => Interlocked.Increment(ref _triggers);
    public void RecordChatBroadcast() => Interlocked.Increment(ref _chatBroadcasts);
    public void RecordAllSilentTrigger() => Interlocked.Increment(ref _allSilentTriggers);
    public void RecordStaleSpeechDiscard() => Interlocked.Increment(ref _staleSpeechDiscards);
    public void RecordStateChangeRetry() => Interlocked.Increment(ref _stateChangeRetries);

    public object Snapshot
    {
        get
        {
            var triggers = Interlocked.Read(ref _triggers);
            var broadcasts = Interlocked.Read(ref _chatBroadcasts);
            var allSilent = Interlocked.Read(ref _allSilentTriggers);
            return new
            {
                triggers,
                chatBroadcasts = broadcasts,
                allSilentTriggers = allSilent,
                staleSpeechDiscards = Interlocked.Read(ref _staleSpeechDiscards),
                stateChangeRetries = Interlocked.Read(ref _stateChangeRetries),
                broadcastRate = triggers == 0 ? 0d : (double)broadcasts / triggers,
                allSilentRate = triggers == 0 ? 0d : (double)allSilent / triggers
            };
        }
    }
}
