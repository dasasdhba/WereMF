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
