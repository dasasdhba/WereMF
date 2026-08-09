using System.Text.Json;

namespace WereMFServer;

public static class NightPatchValidator
{
    private static readonly HashSet<string> BooleanFields = [
        "is_bar_leader", "is_dead", "is_dead_public", "reversed", "myz_threaten",
        "jiaohua_vote_blocked", "shiwu_kidnapped", "jiaohua_protected", "leaf_protected"];

    private static readonly HashSet<string> NumberFields = [
        "smog_count", "capsule_count", "potion_count", "xian_song_count", "bug_count", "jiaohua_blocked"];

    public static bool IsValid(JsonElement root, IReadOnlySet<int> playerIds)
    {
        if (!root.TryGetProperty("message_type", out var messageType) || messageType.GetString() != "public" ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("cause", out var cause) || cause.GetString() != "huika_smog" ||
            !data.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array ||
            entities.GetArrayLength() == 0 ||
            data.EnumerateObject().Any(x => x.Name is not "cause" and not "entities"))
            return false;

        var seen = new HashSet<int>();
        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object ||
                entity.EnumerateObject().Any(x => x.Name is not "player_id" and not "state") ||
                !entity.TryGetProperty("player_id", out var playerId) || !playerId.TryGetInt32(out var id) ||
                !entity.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object ||
                !seen.Add(id) || !playerIds.Contains(id) || state.EnumerateObject().Count() == 0)
                return false;

            foreach (var field in state.EnumerateObject())
            {
                var validType = BooleanFields.Contains(field.Name)
                    ? field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    : NumberFields.Contains(field.Name)
                        ? field.Value.ValueKind == JsonValueKind.Number
                        : field.Name == "dead_showing_name" && field.Value.ValueKind == JsonValueKind.String;
                if (!validType) return false;
            }
        }

        return true;
    }
}
