using System.Text.Json;
using System.Text.Json.Nodes;

namespace WereMFServer;

public static class EnvelopeRedactor
{
    public static JsonElement ForPlayer(JsonElement root, int playerId)
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
                if (item["player"] is JsonObject player && player["anonymous"]?.GetValue<bool>() == true)
                    player["name"] = $"玩家{id}";
                if (id != playerId && item["state"] is JsonObject state)
                    state["is_bar_leader"] = false;
                var role = item["role"] as JsonObject;
                var publicRole = CreatePublicRole(role);
                if (id != playerId) item["role"] = publicRole;
                else if (publicRole is not null && role is not null) role["public_reveal"] = true;
            }
        }
        return JsonSerializer.SerializeToElement(new { type = "game_message", payload });
    }

    private static JsonObject? CreatePublicRole(JsonObject? role)
    {
        if (role is null) return null;
        var charaType = role["chara_type"]?.GetValue<string>();
        var data = role["data"] as JsonObject;
        if (charaType == "叶子" && data?["fury"]?.GetValue<bool>() == true)
            return new JsonObject { ["chara_type"] = "叶子", ["summary_name"] = "叶子", ["data"] = new JsonObject { ["fury"] = true }, ["public_reveal"] = true };
        return null;
    }
}
