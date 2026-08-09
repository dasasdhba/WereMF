using System.Text.Json;
using WereMFServer;

var tests = new (string Name, Action Body)[]
{
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

static void AssertInvalid(string json, IReadOnlySet<int> playerIds)
{
    if (NightPatchValidator.IsValid(JsonSerializer.Deserialize<JsonElement>(json), playerIds))
        throw new InvalidOperationException("expected patch to be rejected");
}
