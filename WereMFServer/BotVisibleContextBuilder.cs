using System.Text.Json;

namespace WereMFServer;

internal sealed class BotVisibleContextBuilder
{
    public string Build(IEnumerable<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> timeline, int playerId, string mode, int take = 24, long phaseStartTimelineSequence = 0)
    {
        var visible = timeline.Where(x => x.RecipientPlayerId is null || x.RecipientPlayerId == playerId).ToArray();
        var currentStateItem = visible.LastOrDefault(x => x.Sequence > phaseStartTimelineSequence && CliMessageRouter.IsAuthoritativeState(x.Envelope));
        var currentState = currentStateItem.Envelope;
        var currentStateSequence = currentStateItem.Sequence;
        var currentPatches = visible
            .Where(x => x.Sequence > currentStateSequence && CliMessageRouter.IsNightPatch(x.Envelope))
            .Select(x => $"公开 #{x.Sequence}：{Compact(x.Envelope)}")
            .ToArray();
        var recent = visible.Where(x => !CliMessageRouter.IsContextSnapshot(x.Envelope)).TakeLast(take);
        var history = string.Join('\n', recent.Select(x =>
            $"{(x.RecipientPlayerId is null ? "公开" : "私密")} #{x.Sequence}：{Compact(x.Envelope)}"));
        if (history.Length > 16_000) history = history[^16_000..];

        var authoritative = currentState.ValueKind == JsonValueKind.Undefined
            ? "尚未收到本阶段状态快照；不得从旧事件臆测当前临时效果。"
            : Compact(currentState);
        if (currentPatches.Length > 0)
            authoritative += "\n【其后仍然有效的公开夜间增量】\n" + string.Join('\n', currentPatches);
        return $"【本局模式】\n{mode}\n\n【当前权威状态】\n{authoritative}\n\n" +
               "以上当前状态绝对正确：历史或记忆与其冲突时一律忽略旧信息，当前状态未显示的临时效果已经失效。\n\n" +
               $"【按接收顺序排列的近期事件（编号越大越新）】\n{history}";
    }

    public static string Compact(JsonElement envelope)
    {
        var raw = envelope.GetRawText();
        var limit = 1_200;
        if (envelope.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("api", out var api) &&
            api.ValueKind == JsonValueKind.String &&
            api.GetString() is "game_update_night" or "game_update_night_patch" or "game_update_day" or "cli_game_summary")
            limit = 8_000;
        return raw.Length <= limit ? raw : raw[..limit] + "…";
    }

    public static void CollectRoleTypes(JsonElement node, List<string> roles)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("chara_type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String &&
                typeNode.GetString() is string role && !roles.Contains(role, StringComparer.OrdinalIgnoreCase)) roles.Add(role);
            foreach (var property in node.EnumerateObject()) CollectRoleTypes(property.Value, roles);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) CollectRoleTypes(item, roles);
    }
}
