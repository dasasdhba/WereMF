using System.Reflection;
using System.Text;

namespace WereMFServer;

internal static class BotGameKnowledge
{
    public static string Rules { get; } = LoadRules();

    public const string Strategy = """
        行为原则：
        - 发言保持克制。投票本身已经是公开表态；但有新增信息、必要推导、当前票型显示自己是唯一最高票时的必要自保、被直接点名且沉默会增加出局风险、拉票必要或必要干扰时，应极简发言。并列最高票或不是当前唯一最高票时，不要仅因为收到投票就公开身份。纯 Bot 场上完全沉默时，有具体可核对信息的玩家应主动开口；没有有效内容时保持沉默，禁止“先看票型”之类无信息句。
        - 私密信息只改变你的内部判断，不自动产生公开义务。只有当公开某个私密结论能带来明确、即时且高于隐藏身份收益的战术收益时，才考虑发言；否则可以 silent，或只根据公开信息发言。valuable_private_information 是可选意图，不是默认必须触发的义务。
        - 精确身份公开不是绝对禁区，也不是必须动作。应综合当前被票风险、公开身份的可信度、对本阵营的即时收益、身份技能剩余价值和被敌方利用的风险；确有必要时可以公开，但只说达成目的所需的最小内容，不要因为“我知道一个身份”就自动报出身份。
        - 吧主知道脚滑人、脚滑人查询到身份、炮仙或合虫获得的私密阵营信息，默认只用于内部投票和技能决策。除非当前局面有明确且更高的公开收益，否则不要主动供出对象或信息来源。
        - 身份保持连续：在当前权威状态明确发生重抽、合虫复制、叶子换阶或其他身份变化前，不得自行改变自己的真实身份。对外伪装身份时也不要在同一局面反复跳换说法；伪装只是话术，不改变你的真实身份。
        - 只说场上尚不存在的信息或推导，不复述别人，不说“我先看看”“大家怎么看”等无信息内容。能一句说完就不要说两句。
        - 第一票尚未使用时，如果场上已经连续一个思考间隔无人发言、无人投票，就从合法玩家中随便投一个，避免半数弃票带来的随机驱逐风险；第二票仍用于确认或根据新信息改票。
        - 重抽身份时，炮仙和脚滑人都是关键且有信息价值的身份，默认应当保留；只有当前局面存在明确而更高的策略收益时才考虑重抽，不能只是为了随机换身份。
        - 生存通常比证明身份重要。被询问身份不等于必须回答；没有明显收益时不要公开精确身份。
        - 面临出局风险时，可先只声称自己属于吧方或爆方；也可伪装成优势阵营中不与公开信息冲突的身份。只有风险很高或收益明显时才公开真实身份。
        - 叶子未暴露时，非叶子玩家受投票威胁可以强调自己不是叶子；必要时再公开真实身份并说明其价值。
        - 若自己是已经公开并进入二阶段的叶子，不再伪装，白天以造成玩家减员为最高目标。投票早期先投威胁较大的合法目标；投票预算不足 60 秒时，根据公开票型改投最可能被投出局的合法玩家。除非需要拉票，否则保持沉默。
        - 公开状态推导优先于单条身份声明，但死亡时展示的身份可能是合虫伪装，不能直接当作原始身份计数。若公开推导显示某玩家只是高可信潜在队友，不要仅因其声称同阵营就相信，也不要仅因该声明就投他；先按公开叶子优先级选择目标。
        - 当前权威状态绝对正确。历史或长期记忆与当前状态冲突时必须忽略旧信息；当前状态未显示的临时效果已经消失。
        """;

    private const string FallbackRules = """
        目标：吧方消灭爆方；爆方消灭吧方。叶子局还需先消灭叶子；叶子要成为唯一存活者。
        白天公开讨论并投票，每人最多投两次，第二次用于确认或改票；大量弃票有随机驱逐弃票者的风险。
        永远以当前请求 JSON 的限制和最新权威状态为准。
        """;

    public static string Focus(string api, IEnumerable<string> activeRoles)
    {
        var roleNames = new[] { "脚滑人", "Doge", "庸医", "地鼠", "兔子", "铯郎", "法猫", "卡比", "粉侠", "爬行者", "炮仙", "实物", "灰卡比", "音魔", "CTF", "合虫", "彩怪", "贤松", "江仙", "myz", "叶子" };
        var roles = activeRoles
            .Where(role => roleNames.Contains(role, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var apiRoleHints = new (string Token, string RuleKeyword)[]
        {
            ("jiaohua", "脚滑人"), ("doge", "Doge"), ("yongyi", "庸医"), ("dishu", "地鼠"),
            ("tuzi", "兔子"), ("selang", "铯郎"), ("famao", "法猫"), ("kabi", "卡比"),
            ("fenxia", "粉侠"), ("paxing", "爬行者"), ("paoxian", "炮仙"), ("shiwu", "实物"),
            ("huikabi", "灰卡比"), ("yinmo", "音魔"), ("ctf", "CTF"), ("hechong", "合虫"),
            ("caiguai", "彩怪"), ("xiansong", "贤松"), ("jiangxian", "江仙"), ("myz", "myz"),
            ("leaf", "叶子")
        };
        foreach (var (token, ruleKeyword) in apiRoleHints)
            if (api.Contains(token, StringComparison.OrdinalIgnoreCase) && !roles.Contains(ruleKeyword, StringComparer.OrdinalIgnoreCase))
                roles.Add(ruleKeyword);

        var allLines = Rules.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
        var focused = new List<string>();
        foreach (var role in roles)
        {
            var definition = allLines.FirstOrDefault(line =>
                (line.Contains($"{role}:", StringComparison.OrdinalIgnoreCase) || line.Contains($"{role}：", StringComparison.OrdinalIgnoreCase)) &&
                line.IndexOf(role, StringComparison.OrdinalIgnoreCase) <= 4);
            if (definition is not null) focused.Add($"【{role}】{definition}");
        }
        if (roles.Contains("脚滑人", StringComparer.OrdinalIgnoreCase))
            focused.Add("【脚滑人信息策略】查询结果优先用于内部判断和行动；只有公开该结果的即时收益明显高于隐藏收益时才公开。valuable_private_information 可选，不会自动使 silent 无效；若公开，只说最小必要结论，不要无理由报精确身份。");
        if (api.Contains("vote", StringComparison.OrdinalIgnoreCase))
            focused.AddRange(allLines.Where(line => line.Contains("至少有一半", StringComparison.Ordinal) || line.Contains("每名玩家在每个白天", StringComparison.Ordinal)).Take(2));
        if (api.Contains("reroll", StringComparison.OrdinalIgnoreCase))
            focused.AddRange(allLines.Where(line => line.Contains("可更改自身身份", StringComparison.Ordinal)).Take(1));
        if (api.Contains("revive", StringComparison.OrdinalIgnoreCase))
            focused.AddRange(allLines.Where(line => line.Contains("救活", StringComparison.Ordinal) || line.Contains("复活", StringComparison.Ordinal)).Take(2));

        var focus = focused.Count == 0
            ? "当前请求没有单独的身份规则段落；严格以当前请求 JSON、权威状态和完整规则为准。"
            : string.Join('\n', focused.Distinct());
        return focus.Length <= 6_000 ? focus : focus[..6_000];
    }    private static string LoadRules()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WereMFServer.BotDesign");
            if (stream is null) return FallbackRules;
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = reader.ReadToEnd();
            var credits = text.IndexOf("\nCREDITS：", StringComparison.Ordinal);
            if (credits < 0) credits = text.IndexOf("\r\nCREDITS：", StringComparison.Ordinal);
            if (credits >= 0) text = text[..credits];
            return text.Trim();
        }
        catch
        {
            return FallbackRules;
        }
    }
}
