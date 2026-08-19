using System.Text;

namespace WereMFServer;

internal static class BotMemoryGuard
{
    private static readonly char[] SentenceEnds = ['。', '！', '？', '\n', '\r'];

    public static string RemoveSelfIdentityClaims(string? summary, int playerId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(summary)) return "";

        var result = new StringBuilder(summary.Length);
        var start = 0;
        for (var i = 0; i <= summary.Length; i++)
        {
            if (i < summary.Length && !SentenceEnds.Contains(summary[i])) continue;
            var sentence = summary[start..i].Trim();
            var delimiter = i < summary.Length ? summary[i] : '\0';
            if (!IsSelfIdentityClaim(sentence, playerId, playerName))
            {
                if (result.Length > 0 && result[^1] != '\n' && sentence.Length > 0) result.Append(' ');
                result.Append(sentence);
                if (delimiter != '\0' && delimiter != '\r') result.Append(delimiter);
            }
            start = i + 1;
        }
        return result.ToString().Trim();
    }

    private static bool IsSelfIdentityClaim(string sentence, int playerId, string playerName)
    {
        if (sentence.Length == 0) return false;
        if (sentence.StartsWith("身份：", StringComparison.Ordinal) ||
            sentence.StartsWith("自身身份：", StringComparison.Ordinal) ||
            sentence.StartsWith("我的身份：", StringComparison.Ordinal) ||
            sentence.StartsWith("阵营：", StringComparison.Ordinal) ||
            sentence.StartsWith("自身阵营：", StringComparison.Ordinal) ||
            sentence.StartsWith("我的阵营：", StringComparison.Ordinal))
            return true;

        var refersToSelf = sentence.Contains(playerName, StringComparison.OrdinalIgnoreCase) ||
                           sentence.Contains($"{playerId} 号", StringComparison.Ordinal) ||
                           sentence.Contains($"{playerId}号", StringComparison.Ordinal) ||
                           sentence.Contains("我", StringComparison.Ordinal);
        if (!refersToSelf) return false;

        return sentence.Contains("角色为", StringComparison.Ordinal) ||
               sentence.Contains("身份是", StringComparison.Ordinal) ||
               sentence.Contains("身份为", StringComparison.Ordinal) ||
               sentence.Contains("属于吧方", StringComparison.Ordinal) ||
               sentence.Contains("属于爆方", StringComparison.Ordinal) ||
               sentence.Contains("属于火纯方", StringComparison.Ordinal);
    }
}
