namespace WereMFServer;

internal static class GameLogAssembler
{
    public static string Merge(string cliLog, IReadOnlyDictionary<int, List<string>> dayInteractions)
    {
        if (dayInteractions.Count == 0 || string.IsNullOrEmpty(cliLog)) return cliLog;
        var newline = cliLog.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = cliLog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var merged = new List<string>(lines.Length + dayInteractions.Values.Sum(x => x.Count + 1));
        var day = 0;
        var waitingForVoteRequest = false;
        foreach (var line in lines)
        {
            merged.Add(line);
            if (line == "[Public] 投票开始" || line.Contains("\"api\":\"vote_start_broadcast\"", StringComparison.Ordinal))
            {
                day++;
                waitingForVoteRequest = true;
            }
            else if (waitingForVoteRequest && (line.StartsWith("[Internal] 输入 x y 表示", StringComparison.Ordinal) ||
                                               line.Contains("\"api\":\"request_vote\"", StringComparison.Ordinal)))
            {
                if (dayInteractions.TryGetValue(day, out var interactions) && interactions.Count > 0)
                {
                    merged.Add($"[Server] 第 {day} 天聊天与投票记录");
                    merged.AddRange(interactions);
                }
                waitingForVoteRequest = false;
            }
        }
        return string.Join(newline, merged);
    }
}
