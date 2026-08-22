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

    public static string BuildPublicFactionDeductions(
        IEnumerable<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> timeline,
        int playerId)
    {
        var visible = timeline
            .Where(x => x.RecipientPlayerId is null || x.RecipientPlayerId == playerId)
            .ToArray();
        var state = visible
            .Where(x => CliMessageRouter.IsAuthoritativeState(x.Envelope))
            .Select(x => x.Envelope)
            .LastOrDefault();
        if (state.ValueKind == JsonValueKind.Undefined || !TryGetEntities(state, out var entities))
            return "【公开状态推导】\n暂无本阶段可用于阵营推导的完整状态。";

        var mode = TryGetModeCounts(visible, out var playerCount, out var barCount, out var boomCount, out var leafCount, out var leafMode);
        var alive = new List<(int Id, string Name)>();
        var publicLeaves = new List<(int Id, string Name)>();
        var displayedBar = 0;
        var displayedBoom = 0;
        var displayedLeaf = 0;

        foreach (var entity in entities.EnumerateArray())
        {
            if (!entity.TryGetProperty("player", out var player) ||
                !player.TryGetProperty("id", out var idNode) ||
                !idNode.TryGetInt32(out var id))
                continue;
            string name = player.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String
                ? nameNode.GetString() ?? $"玩家{id}"
                : $"玩家{id}";
            var stateNode = entity.TryGetProperty("state", out var currentState) && currentState.ValueKind == JsonValueKind.Object
                ? currentState
                : default;
            var isDead = stateNode.ValueKind == JsonValueKind.Object &&
                         stateNode.TryGetProperty("is_dead", out var deadNode) &&
                         deadNode.ValueKind == JsonValueKind.True;
            var role = entity.TryGetProperty("role", out var roleNode) && roleNode.ValueKind == JsonValueKind.Object
                ? GetRoleType(roleNode)
                : null;
            var publicReveal = entity.TryGetProperty("role", out var publicRoleNode) &&
                               publicRoleNode.ValueKind == JsonValueKind.Object &&
                               publicRoleNode.TryGetProperty("public_reveal", out var revealNode) &&
                               revealNode.ValueKind == JsonValueKind.True;

            if (!isDead) alive.Add((id, name));
            if (!isDead && publicReveal && string.Equals(role, "叶子", StringComparison.OrdinalIgnoreCase))
                publicLeaves.Add((id, name));

            if (!isDead || stateNode.ValueKind != JsonValueKind.Object ||
                !stateNode.TryGetProperty("is_dead_public", out var publicDeadNode) ||
                publicDeadNode.ValueKind != JsonValueKind.True ||
                !stateNode.TryGetProperty("dead_showing_name", out var showingNode) ||
                showingNode.ValueKind != JsonValueKind.String)
                continue;

            var shownRole = showingNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(shownRole)) continue;
            if (BarRoles.Contains(shownRole)) displayedBar++;
            else if (BoomRoles.Contains(shownRole)) displayedBoom++;
            else if (string.Equals(shownRole, "叶子", StringComparison.OrdinalIgnoreCase)) displayedLeaf++;
        }

        var lines = new List<string>();
        if (publicLeaves.Count > 0)
        {
            lines.Add($"当前确认公开叶子：{string.Join("、", publicLeaves.Select(x => $"{x.Id}号"))}（公开叶子不是普通吧/爆方目标）");
            if (alive.Any(x => !publicLeaves.Any(leaf => leaf.Id == x.Id)))
                lines.Add("非叶子阵营的默认目标优先级：公开叶子优先于仅凭公开声明怀疑的其他玩家；除非有明确即时战术理由，不要先投可能的同阵营声明者。");
        }

        if (displayedBar > 0 || displayedBoom > 0 || displayedLeaf > 0)
            lines.Add($"公开死亡展示身份统计：吧方样式 {displayedBar}、爆方样式 {displayedBoom}、叶子样式 {displayedLeaf}。死亡展示身份可能来自合虫伪装，只能作为证据，不能直接当作原始身份或绝对阵营计数。");

        if (mode && leafMode && publicLeaves.Count == leafCount && leafCount > 0)
        {
            var aliveNonLeaf = alive.Where(x => !publicLeaves.Any(leaf => leaf.Id == x.Id)).ToArray();
            var inferredBoomRemaining = boomCount - displayedBoom;
            var inferredBarRemaining = barCount - displayedBar;
            if (aliveNonLeaf.Length > 0 && inferredBoomRemaining == aliveNonLeaf.Length && inferredBarRemaining == 0)
            {
                lines.Add($"【高可信但非绝对的公开推导】按当前 {playerCount} 人叶子局配额与死亡展示，存活非叶子玩家 {string.Join("、", aliveNonLeaf.Select(x => $"{x.Id}号"))} 更可能同属爆方；合虫死亡展示伪装仍可能使该计数失真，不能把它当作服务器确认身份。");
                if (aliveNonLeaf.Length == 2)
                    lines.Add("在这个推导成立时，两名存活非叶子玩家应优先协同处理公开叶子，而不是仅凭一方的身份声明互投。");
            }
        }

        return lines.Count == 0
            ? "【公开状态推导】当前没有足够的公开事实形成稳定阵营结论；不要把死亡展示身份当成绝对身份。"
            : "【公开状态推导（每次根据当前权威状态重算，不写入 memory）】\n" + string.Join('\n', lines);
    }

    private static string? GetRoleType(JsonElement role)
    {
        return role.TryGetProperty("chara_type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String
            ? typeNode.GetString()
            : null;
    }

    private static bool TryGetEntities(JsonElement envelope, out JsonElement entities)
    {
        entities = default;
        if (!envelope.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("data", out var data))
            return false;
        if (data.ValueKind == JsonValueKind.Array)
        {
            entities = data;
            return true;
        }
        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("entities", out entities) && entities.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetModeCounts(
        IEnumerable<(long Sequence, int? RecipientPlayerId, JsonElement Envelope)> timeline,
        out int playerCount,
        out int barCount,
        out int boomCount,
        out int leafCount,
        out bool leafMode)
    {
        playerCount = barCount = boomCount = leafCount = 0;
        leafMode = false;
        foreach (var item in timeline.Reverse())
        {
            if (!item.Envelope.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("api", out var apiNode) ||
                apiNode.ValueKind != JsonValueKind.String ||
                !string.Equals(apiNode.GetString(), "game_mode_broadcast", StringComparison.Ordinal) ||
                !payload.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
                continue;

            playerCount = ReadInt(data, "player_count");
            barCount = ReadInt(data, "bar_count");
            boomCount = ReadInt(data, "boom_count");
            leafCount = ReadInt(data, "leaf_count");
            leafMode = data.TryGetProperty("mode", out var modeNode) &&
                       modeNode.ValueKind == JsonValueKind.String &&
                       string.Equals(modeNode.GetString(), "leaf", StringComparison.OrdinalIgnoreCase);
            return playerCount > 0 && barCount >= 0 && boomCount >= 0 && leafCount >= 0;
        }
        return false;
    }

    private static int ReadInt(JsonElement node, string property)
    {
        return node.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
    }
}
