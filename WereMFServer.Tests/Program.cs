using System.Text.Json;
using WereMFServer;

var tests = new (string Name, Action Body)[]
{
    ("classifies CLI envelopes without interpreting their payload", () =>
    {
        Assert(CliEnvelope.TryParse("{\"api\":\"game_update_night_patch\",\"message_type\":\"public\",\"data\":{}}", out var patch) && patch is not null, "valid CLI JSON must parse");
        Assert(CliMessageRouter.Classify(patch!) == CliRouteKind.NightPatch, "night patch must use the dedicated route");
        Assert(CliMessageRouter.Classify(ParseEnvelope("{\"api\":\"request_vote\",\"message_type\":\"public\",\"data\":[]}")) == CliRouteKind.ConcurrentRequest, "vote requests must use concurrent routing");
        Assert(CliMessageRouter.Classify(ParseEnvelope("{\"api\":\"game_update_day\",\"message_type\":\"public\",\"data\":[]}")) == CliRouteKind.Snapshot, "full state updates must use snapshot routing");
    }),
    ("recognizes malformed CLI lines without throwing", () =>
    {
        Assert(!CliEnvelope.TryParse("not json", out _), "malformed CLI JSON must be rejected");
        Assert(CliMessageRouter.IsRequestEnvelope(JsonSerializer.Deserialize<JsonElement>("{\"type\":\"game_message\",\"payload\":{\"api\":\"request_vote\"}}")), "request envelopes must be recognized");
    }),
    ("blocks daytime speech until this phase has a day snapshot", () =>
    {
        var gate = new BotSpeechStateGate();
        gate.BeginDay(10);
        Assert(!gate.TryCapture(out _), "a new day must not inherit the prior phase's snapshot");

        var timeline = new[]
        {
            (9L, (int?)null, JsonSerializer.Deserialize<JsonElement>("{\"type\":\"game_message\",\"payload\":{\"api\":\"game_update_day\",\"data\":[{\"state\":{\"old_effect\":true}}]}}"))
        };
        var context = new BotVisibleContextBuilder().Build(timeline, 1, "测试模式", phaseStartTimelineSequence: 10);
        Assert(context.Contains("尚未收到本阶段状态快照", StringComparison.Ordinal), "context must not select a snapshot before the phase boundary");

        gate.MarkDaySnapshotReady();
        Assert(gate.TryCapture(out var state), "speech may start only after the new day snapshot is recorded");
        Assert(state.PhaseStartTimelineSequence == 10, "speech context must retain the day boundary sequence");
        gate.BeginDaySnapshotUpdate();
        Assert(!gate.TryCapture(out _), "an in-flight full-state update must close the gate before its new snapshot is recorded");
    }),
    ("invalidates a delayed speech result after authoritative state changes", () =>
    {
        var gate = new BotSpeechStateGate();
        gate.BeginDay(20);
        gate.MarkDaySnapshotReady();
        Assert(gate.TryCapture(out var pending), "test setup needs an initial speech state");

        // Model response is delayed while a valid state patch/full snapshot arrives.
        gate.InvalidateAuthoritativeState();
        Assert(!gate.IsCurrent(pending), "a delayed result based on an older authoritative state must be discarded");
        Assert(gate.TryCapture(out var retry), "one bounded retry may capture the fresh state");
        Assert(gate.IsCurrent(retry), "fresh retry state must be broadcastable");

        gate.EndPhase();
        Assert(!gate.IsCurrent(retry), "a result may not cross into a later phase");
    }),
    ("reports aggregate conversation outcomes without sensitive data", () =>
    {
        var metrics = new BotConversationMetrics();
        metrics.RecordTrigger();
        metrics.RecordTrigger();
        metrics.RecordChatBroadcast();
        metrics.RecordChatBroadcast();
        metrics.RecordChatBroadcast();
        metrics.RecordAllSilentTrigger();
        metrics.RecordStaleSpeechDiscard();
        metrics.RecordStaleSpeechDiscard();
        metrics.RecordStateChangeRetry();

        using var client = new LlmBotClient("http://127.0.0.1:1/", null, "test", 1, 64);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(client.StatsWithConversationMetrics(metrics)));
        var stats = json.RootElement;
        Assert(stats.TryGetProperty("requests", out _) && stats.TryGetProperty("speechSilences", out _) && stats.TryGetProperty("fallbackStats", out _), "legacy LLM and fallback statistics must remain available");
        var conversation = stats.GetProperty("conversationStats");
        Assert(conversation.GetProperty("triggers").GetInt64() == 2, "triggers must count orchestration runs, not model calls");
        Assert(conversation.GetProperty("chatBroadcasts").GetInt64() == 3, "chat broadcasts must count actual user-visible sends");
        Assert(conversation.GetProperty("allSilentTriggers").GetInt64() == 1, "all-silent count must describe completed triggers");
        Assert(conversation.GetProperty("staleSpeechDiscards").GetInt64() == 2 && conversation.GetProperty("stateChangeRetries").GetInt64() == 1, "state-gate discards and retries must be distinct");
        Assert(conversation.GetProperty("broadcastRate").GetDouble() == 1.5 && conversation.GetProperty("allSilentRate").GetDouble() == 0.5, "rates must be derived from aggregate trigger outcomes");
        var allowedFields = new HashSet<string>(["triggers", "chatBroadcasts", "allSilentTriggers", "staleSpeechDiscards", "stateChangeRetries", "broadcastRate", "allSilentRate"]);
        Assert(conversation.EnumerateObject().All(property => allowedFields.Contains(property.Name)), "health aggregates must not serialize prompts, responses, identities, rooms, roles, or state payloads");
    }),
    ("requires valuable local information disclosure without unnecessary identity exposure", () =>
    {
        var focus = BotGameKnowledge.Focus("speech", ["脚滑人"]);
        var prompt = new BotSpeechContext(2, "脚滑 Bot", "白天刚开始", "【当前权威状态】\n仅该 Bot 可见的私密技能结果", "投票尚未开始", focus).ToPrompt();
        Assert(BotGameKnowledge.Strategy.Contains("valuable_private_information", StringComparison.Ordinal), "strategy must expose a dedicated valuable-local-information intent");
        Assert(prompt.Contains("自身合法私密身份、状态、技能结果或事件", StringComparison.Ordinal) && prompt.Contains("silent 无效", StringComparison.Ordinal), "speech prompt must make valuable local disclosure mandatory");
        Assert(focus.Contains("【脚滑人主动公开】", StringComparison.Ordinal) && focus.Contains("精确身份", StringComparison.Ordinal), "脚滑人 focus must require disclosure while minimizing identity exposure");
    }),
    ("limits pure-Bot all-silent opening fallback to one speaker", () =>
    {
        var bots = new[] { 1, 2, 3 };
        var allSilent = new BotConversationDecision?[] { new("", null), new("", null), new("", null) };
        var selected = BotConversationPolicy.SelectOpeningFallbackSpeaker(bots, allSilent);
        Assert(selected is >= 0 and < 3, "all-silent opening may select exactly one eligible fallback speaker");
        Assert(BotConversationPolicy.SelectOpeningFallbackSpeaker(bots, [new BotConversationDecision("已有有效信息", null), new("", null)]) is null, "a mixed opening must not add a group fallback");
        Assert(BotConversationPolicy.SelectOpeningFallbackSpeaker(Array.Empty<int>(), allSilent) is null, "no eligible Bot means no fallback");
    }),
    ("uses required response mode for directly named Bot", () =>
    {
        Assert(BotConversationPolicy.ResponseModeFor("请 脚滑 Bot 回应昨夜结果", "脚滑 Bot") == BotSpeechResponseMode.Required, "directly named Bot must not use optional response mode");
        Assert(BotConversationPolicy.ResponseModeFor("一般讨论", "脚滑 Bot") == BotSpeechResponseMode.Optional, "ordinary discussion must remain optional");
        Assert(BotConversationPolicy.RequiredFallbackText(BotSpeechResponseMode.Required).Length > 0, "required mode needs a short non-silent fallback");
    }),
    ("reserves the capped speech selection for a required named Bot", () =>
    {
        // This list is deliberately in model-completion order: both optional decisions finish
        // before the directly named Bot's required response.
        var completed = new[]
        {
            new BotSpeechCandidate<int>(1, new("可选 A", null), BotSpeechResponseMode.Optional),
            new BotSpeechCandidate<int>(2, new("可选 B", null), BotSpeechResponseMode.Optional),
            new BotSpeechCandidate<int>(3, new("点名回应", null), BotSpeechResponseMode.Required)
        };
        var selected = BotConversationPolicy.SelectSpeechCandidates(completed, speechLimit: 2);
        Assert(selected.Count <= 2, "speech selection must retain the anti-spam cap");
        Assert(selected.Any(x => x.Bot == 3), "a required named response must not be starved by earlier optional completions");
        Assert(selected.Count(x => x.ResponseMode == BotSpeechResponseMode.Optional) == 1, "only the remaining slot may be filled by an optional response");
    }),
    ("routes a current redacted snapshot to a disconnected takeover Bot", () =>
    {
        var connectedHuman = new PlayerSession(1, "在线玩家", false, null);
        var disconnectedTakeover = new PlayerSession(2, "断线托管", false, null) { Connected = false, IsBot = true };
        var permanentlyDeparted = new PlayerSession(3, "已离席", false, null) { Connected = false, IsBot = true, HasLeft = true };

        var recipients = BotSnapshotRouting.ActiveSessions([connectedHuman, disconnectedTakeover, permanentlyDeparted]);
        Assert(recipients.Select(x => x.GameId).Order().SequenceEqual([1, 2]), "a disconnected takeover Bot must receive a stored snapshot, while departed seats are excluded");

        const string input = """
        {"api":"game_update_day","message_type":"public","data":[
          {"player":{"id":2,"name":"断线托管"},"role":{"chara_type":"脚滑人","data":{"private_hint":true}},"state":{"is_dead":false}}
        ]}
        """;
        var redacted = CliRouteTransforms.RedactSnapshot(JsonSerializer.Deserialize<JsonElement>(input), disconnectedTakeover.GameId);
        disconnectedTakeover.History.Add(redacted);
        Assert(disconnectedTakeover.History.Count == 1 &&
               disconnectedTakeover.History[0].GetProperty("payload").GetProperty("data")[0].GetProperty("role").GetProperty("data").GetProperty("private_hint").GetBoolean(),
            "the takeover Bot's stored view must contain its own per-player redacted snapshot before a speech decision");
    }),
    ("accepts a public smog patch with supported public field types", () =>
        AssertValid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"smog_count":1,"is_dead":false,"dead_showing_name":"玩家3"}}
        ]}}
        """, Set(3))),
    ("rejects a non-public patch", () =>
        AssertInvalid("""
        {"message_type":"player_3","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"smog_count":1}}
        ]}}
        """, Set(3))),
    ("rejects a wrong cause or extra data field", () =>
    {
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"other","entities":[
          {"player_id":3,"state":{"smog_count":1}}
        ]}}
        """, Set(3));
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[],"role":"myz"}}
        """, Set(3));
    }),
    ("rejects private, unknown, and incorrectly typed state fields", () =>
    {
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"role":"叶子"}}
        ]}}
        """, Set(3));
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"smog_count":"1"}}
        ]}}
        """, Set(3));
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"player":{"id":3}}}
        ]}}
        """, Set(3));
    }),
    ("rejects duplicate, unknown, and malformed entities", () =>
    {
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{"smog_count":1}},
          {"player_id":3,"state":{"is_dead":true}}
        ]}}
        """, Set(3));
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":99,"state":{"smog_count":1}}
        ]}}
        """, Set(3));
        AssertInvalid("""
        {"message_type":"public","data":{"cause":"huika_smog","entities":[
          {"player_id":3,"state":{}}
        ]}}
        """, Set(3));
    }),
    ("redacts snapshots differently for each recipient without leaking other roles", () =>
    {
        const string input = """
        {"api":"game_update_night","message_type":"public","data":[
          {"player":{"id":1,"name":"Alice","anonymous":true},"role":{"chara_type":"myz","data":{"secret":true}},"state":{"is_bar_leader":true}},
          {"player":{"id":2,"name":"Bob","anonymous":true},"role":{"chara_type":"叶子","data":{"fury":true,"secret":"hidden"}},"state":{"is_bar_leader":true}}
        ]}
        """;
        var alice = CliRouteTransforms.RedactSnapshot(JsonSerializer.Deserialize<JsonElement>(input), 1);
        var bob = CliRouteTransforms.RedactSnapshot(JsonSerializer.Deserialize<JsonElement>(input), 2);
        var aliceEntities = alice.GetProperty("payload").GetProperty("data");
        var bobEntities = bob.GetProperty("payload").GetProperty("data");
        Assert(aliceEntities[0].GetProperty("player").GetProperty("name").GetString() == "玩家1", "anonymous names must be redacted");
        var publicLeafRole = aliceEntities[1].GetProperty("role");
        Assert(publicLeafRole.GetProperty("chara_type").GetString() == "叶子" && !publicLeafRole.GetProperty("data").TryGetProperty("secret", out _), "Alice may see only Bob's public Leaf reveal");
        Assert(aliceEntities[1].GetProperty("state").GetProperty("is_bar_leader").GetBoolean() == false, "Alice must not see Bob's bar leader flag");
        Assert(aliceEntities[0].GetProperty("role").GetProperty("data").GetProperty("secret").GetBoolean(), "Alice must see her own role data");
        Assert(IsNullOrMissing(bobEntities[0], "role"), "Bob must not see Alice's role");
        Assert(bobEntities[1].GetProperty("role").GetProperty("public_reveal").GetBoolean(), "publicly revealed Leaf role must retain its reveal marker");
        Assert(bobEntities[1].GetProperty("role").GetProperty("data").GetProperty("secret").GetString() == "hidden", "own private role data must remain available");
    }),
    ("transforms anonymous and pending routes without changing CLI payload data", () =>
    {
        var anonymous = JsonSerializer.Deserialize<JsonElement>("""
        {"api":"player_anonymous_init","message_type":"internal","data":[
          {"id":2,"name":"Alice","anonymous":true},
          {"id":1,"name":"Unknown","anonymous":true}
        ]}
        """);
        Assert(CliRouteTransforms.TryCreateAnonymousPayload(anonymous, new HashSet<string>(["Alice"]), out var anonymousPayload, out var mappings), "anonymous payload must be transformed");
        Assert(mappings.Count == 1 && mappings[0].Name == "Alice" && mappings[0].PlayerId == 2, "only known sessions may be remapped");
        Assert(anonymousPayload.GetProperty("message_type").GetString() == "public", "anonymous mapping must be public");
        Assert(anonymousPayload.GetProperty("data")[0].GetProperty("name").GetString() == "玩家2", "known anonymous names must be rewritten");
        Assert(anonymousPayload.GetProperty("data")[1].GetProperty("name").GetString() == "Unknown", "unknown names must remain unchanged");

        var pending = JsonSerializer.Deserialize<JsonElement>("""
        {"api":"pending_skill_created","message_type":"internal","data":{"id":"skill-1","type":"Doge"}}
        """);
        var targeted = CliRouteTransforms.CreatePlayerTargetPayload(pending, 2);
        Assert(targeted.GetProperty("message_type").GetString() == "player_2", "pending skills must target their source player");
        Assert(targeted.GetProperty("data").GetProperty("id").GetString() == "skill-1", "pending payload data must be preserved");
    })
};

var failures = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} night patch validator tests passed");
return failures == 0 ? 0 : 1;

static void AssertValid(string json, IReadOnlySet<int> playerIds)
{
    if (!NightPatchValidator.IsValid(JsonSerializer.Deserialize<JsonElement>(json), playerIds))
        throw new InvalidOperationException("expected patch to be valid");
}

static IReadOnlySet<int> Set(params int[] ids) => ids.ToHashSet();

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool IsNullOrMissing(JsonElement element, string property)
{
    return !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;
}

static void AssertInvalid(string json, IReadOnlySet<int> playerIds)
{
    if (NightPatchValidator.IsValid(JsonSerializer.Deserialize<JsonElement>(json), playerIds))
        throw new InvalidOperationException("expected patch to be rejected");
}

static CliEnvelope ParseEnvelope(string json)
{
    if (!CliEnvelope.TryParse(json, out var envelope) || envelope is null) throw new InvalidOperationException("expected valid CLI envelope");
    return envelope;
}
