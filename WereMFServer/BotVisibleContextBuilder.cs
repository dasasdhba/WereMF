using System.Text.Json;

namespace WereMFServer;

internal sealed class BotVisibleContextBuilder
{
    private static readonly HashSet<string> BarRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "脚滑人", "Doge", "庸医", "地鼠", "兔子", "铯郎", "法猫", "卡比", "粉侠", "爬行者"
    };

    private static readonly HashSet<string> BoomRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "炮仙", "实物", "灰卡比", "音魔", "CTF", "合虫", "彩怪", "贤松", "江仙", "myz"
    };

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
        var privateKnowledge = BuildPrivateKnowledge(timeline, playerId);
        return $"【本局模式】\n{mode}\n\n{privateKnowledge}\n\n【当前权威状态】\n{authoritative}\n\n" +
               "以上当前状态绝对正确：历史或记忆与其冲突时一律忽略旧信息，当前状态未显示的临时效果已经失效。\n\n" +
               $"【按接收顺序排列的近期事件（编号越大越新）】\n{history}";
    }

    public static string BuildPrivateKnowledge(
        IEnumerable<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> timeline,
        int playerId)
    {
        var facts = timeline
            .Where(x => x.RecipientPlayerId == playerId)
            .Select(x => ExtractPrivateKnowledge(x.Envelope))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (facts.Length == 0)
            return "【服务器确认的长期私密知识】\n暂无开局或技能产生的可持续私密事实。";

        return "【服务器确认的长期私密知识（仅你可见）】\n" +
               string.Join('\n', facts.Select(x => $"- {x}")) +
               "\n这些是服务器直接告知你的事实，不是推测；除非后续明确出现身份变化或新的同类通知，不要遗忘或自行改写。";
    }

    private static string? ExtractPrivateKnowledge(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("api", out var apiNode) ||
            apiNode.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("message_content", out var contentNode) ||
            contentNode.ValueKind != JsonValueKind.String)
            return null;

        var api = apiNode.GetString();
        var content = contentNode.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return null;

        return api switch
        {
            "barleader_notify" => $"吧主开局获知：{content}",
            "jiaohua_start_notify" => $"脚滑人开局获知：{content}",
            "paoxian_party_notify" => $"炮仙获知队友：{content.Replace("队友：", "", StringComparison.Ordinal).Trim()}",
            "xiansong_start_notify" => $"贤松开局获知：{content}",
            _ => null
        };
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

    public static string BuildAuthoritativeSelfIdentity(
        IEnumerable<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> timeline,
        int playerId,
        string playerName)
    {
        foreach (var item in timeline
                     .Where(x => x.RecipientPlayerId is null || x.RecipientPlayerId == playerId)
                     .Reverse())
        {
            if (!CliMessageRouter.IsAuthoritativeState(item.Envelope) ||
                !item.Envelope.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entity in data.EnumerateArray())
            {
                if (!entity.TryGetProperty("player", out var player) ||
                    !player.TryGetProperty("id", out var id) ||
                    !id.TryGetInt32(out var candidateId) || candidateId != playerId ||
                    !entity.TryGetProperty("role", out var role) ||
                    role.ValueKind != JsonValueKind.Object ||
                    !role.TryGetProperty("chara_type", out var typeNode) ||
                    typeNode.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(typeNode.GetString()))
                    continue;

                var roleType = typeNode.GetString()!;
                var roleName = role.TryGetProperty("summary_name", out var summaryName) && summaryName.ValueKind == JsonValueKind.String
                    ? summaryName.GetString()
                    : roleType;
                var faction = BarRoles.Contains(roleType) ? "吧方"
                    : BoomRoles.Contains(roleType) ? "爆方"
                    : roleType.Equals("叶子", StringComparison.OrdinalIgnoreCase) ? "火纯方"
                    : null;
                var factionText = faction is null ? "" : $"，阵营：{faction}";
                return $"【服务器确认的自我身份（最高优先级）】\n{playerId} 号玩家“{playerName}”当前身份：{roleName}{factionText}。此项来自服务器状态，不得被聊天、推测或长期记忆覆盖。";
            }
        }

        return $"【服务器确认的自我身份（最高优先级）】\n{playerId} 号玩家“{playerName}”。当前状态未提供可确认的身份，不得从他人身份或公开事件推测自己的身份与阵营。";
    }
}
