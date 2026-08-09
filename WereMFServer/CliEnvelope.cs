using System.Text.Json;

namespace WereMFServer;

public enum CliRouteKind
{
    Public,
    Player,
    Internal,
    Request,
    Snapshot,
    NightPatch,
    ParseError,
    PendingSkill,
    AnonymousMapping,
    ConcurrentRequest,
    Log,
    NextGame,
    Ignored
}

public sealed record CliEnvelope(JsonElement Root, string Api, string Target)
{
    public static bool TryParse(string line, out CliEnvelope? envelope)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement.Clone();
            var target = root.TryGetProperty("message_type", out var messageType) && messageType.ValueKind == JsonValueKind.String
                ? messageType.GetString() ?? "internal"
                : "internal";
            var api = root.TryGetProperty("api", out var apiNode) && apiNode.ValueKind == JsonValueKind.String
                ? apiNode.GetString() ?? ""
                : "";
            envelope = new CliEnvelope(root, api, target);
            return true;
        }
        catch (JsonException)
        {
            envelope = null;
            return false;
        }
    }

    public JsonElement GameMessage => JsonSerializer.SerializeToElement(new { type = "game_message", payload = Root });
}

public static class CliMessageRouter
{
    public static CliRouteKind Classify(CliEnvelope envelope)
    {
        var api = envelope.Api;
        if (api == "request_player_list") return CliRouteKind.Ignored;
        if (api == "cli_log") return CliRouteKind.Log;
        if (api == "request_for_next_game") return CliRouteKind.NextGame;
        if (api == "game_update_night_patch") return CliRouteKind.NightPatch;
        if (api == "player_anonymous_init") return CliRouteKind.AnonymousMapping;
        if (api == "pending_skill_created") return CliRouteKind.PendingSkill;
        if (api is "request_reroll_player" or "request_vote") return CliRouteKind.ConcurrentRequest;
        if (api.EndsWith("_parse_error", StringComparison.Ordinal)) return CliRouteKind.ParseError;
        if (IsSnapshot(envelope)) return CliRouteKind.Snapshot;
        if (api.StartsWith("request_", StringComparison.Ordinal)) return CliRouteKind.Request;
        return envelope.Target == "public" ? CliRouteKind.Public : envelope.Target.StartsWith("player_", StringComparison.Ordinal) ? CliRouteKind.Player : CliRouteKind.Internal;
    }

    public static bool IsSnapshot(CliEnvelope envelope) =>
        envelope.Target == "public" && envelope.Api is "game_update_night" or "game_update_day" or "cli_game_summary";

    public static bool IsAuthoritativeState(JsonElement envelope) =>
        TryEnvelopeApi(envelope) is "game_update_night" or "game_update_day";

    public static bool IsNightPatch(JsonElement envelope) => TryEnvelopeApi(envelope) == "game_update_night_patch";

    public static bool IsContextSnapshot(JsonElement envelope) =>
        IsAuthoritativeState(envelope) || IsNightPatch(envelope) || TryEnvelopeApi(envelope) == "game_mode_broadcast";

    public static bool IsRequestEnvelope(JsonElement envelope) =>
        envelope.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String && type.GetString() == "game_message" &&
        envelope.TryGetProperty("payload", out var payload) &&
        payload.TryGetProperty("api", out var api) &&
        api.ValueKind == JsonValueKind.String &&
        api.GetString()!.StartsWith("request_", StringComparison.Ordinal);

    public static string? TryEnvelopeApi(JsonElement envelope) =>
        envelope.TryGetProperty("payload", out var payload) &&
        payload.TryGetProperty("api", out var api) &&
        api.ValueKind == JsonValueKind.String
            ? api.GetString()
            : null;
}
