using System.Text.Json;
using System.Text.Json.Nodes;

namespace WereMFServer;

public sealed record AnonymousPlayerMapping(string Name, int PlayerId);

public static class CliRouteTransforms
{
    public static JsonElement CreatePlayerTargetPayload(JsonElement root, int playerId)
    {
        var payload = JsonNode.Parse(root.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("CLI payload must be a JSON object");
        payload["message_type"] = $"player_{playerId}";
        return payload.Deserialize<JsonElement>();
    }

    public static bool TryCreateAnonymousPayload(
        JsonElement root,
        IReadOnlySet<string> knownPlayerNames,
        out JsonElement payload,
        out IReadOnlyList<AnonymousPlayerMapping> mappings)
    {
        payload = default;
        mappings = [];
        var node = JsonNode.Parse(root.GetRawText());
        if (node is not JsonObject payloadObject || payloadObject["data"] is not JsonArray players)
            return false;

        var result = new List<AnonymousPlayerMapping>();
        foreach (var item in players.OfType<JsonObject>())
        {
            if (item["id"] is not JsonValue idNode || !idNode.TryGetValue<int>(out var playerId) ||
                item["name"] is not JsonValue nameNode || !nameNode.TryGetValue<string>(out var name) ||
                !knownPlayerNames.Contains(name))
                continue;

            item["name"] = $"玩家{playerId}";
            result.Add(new AnonymousPlayerMapping(name, playerId));
        }

        payloadObject["message_type"] = "public";
        payload = payloadObject.Deserialize<JsonElement>();
        mappings = result;
        return true;
    }

    public static JsonElement RedactSnapshot(JsonElement root, int playerId) =>
        EnvelopeRedactor.ForPlayer(root, playerId);
}
