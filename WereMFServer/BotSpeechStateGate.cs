namespace WereMFServer;

// Timeline history remains useful for context, but it must never make a
// previous phase's snapshot look current to a daytime speech.
internal sealed class BotSpeechStateGate
{
    private int _phase;
    private long _revision;
    private long _phaseStartTimelineSequence;
    private bool _daySnapshotReady;

    public void Reset(long timelineSequence)
    {
        _phase++;
        _revision++;
        _phaseStartTimelineSequence = timelineSequence;
        _daySnapshotReady = false;
    }

    public void BeginDay(long timelineSequence) => Reset(timelineSequence);

    public void EndPhase()
    {
        _phase++;
        _revision++;
        _daySnapshotReady = false;
    }

    public void MarkDaySnapshotReady()
    {
        _daySnapshotReady = true;
        _revision++;
    }

    public void BeginDaySnapshotUpdate()
    {
        _daySnapshotReady = false;
        _revision++;
    }

    public void InvalidateAuthoritativeState() => _revision++;

    public bool TryCapture(out BotSpeechState state)
    {
        state = new BotSpeechState(_phase, _revision, _phaseStartTimelineSequence);
        return _daySnapshotReady;
    }

    public bool IsCurrent(BotSpeechState state) =>
        _daySnapshotReady && state.Phase == _phase && state.Revision == _revision;
}

internal sealed record BotSpeechState(int Phase, long Revision, long PhaseStartTimelineSequence);

internal static class BotSnapshotRouting
{
    // A temporarily disconnected human may be taken over by an LLM Bot.  It
    // still needs its own redacted snapshot even though SendAsync will
    // correctly no-op without a live socket.  Permanently departed seats do
    // not make Bot decisions and must not retain new private state.
    public static PlayerSession[] ActiveSessions(IEnumerable<PlayerSession> sessions) =>
        sessions.Where(x => !x.HasLeft).ToArray();
}
