using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WereMFServer;

internal sealed class LlmBotClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly SemaphoreSlim _concurrency = new(4, 4);
    private long _requests;
    private long _successes;
    private long _failures;
    private long _accepted;
    private long _rejected;
    private long _speechRequests;
    private long _speechSuccesses;
    private long _speechFailures;
    private long _speechSilences;
    private long _speechMessages;
    private long _memoryRequests;
    private long _memorySuccesses;
    private long _memoryFailures;

    public LlmBotClient(string endpoint, string apiKey, string model, int timeoutSeconds)
    {
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public object Stats => new
    {
        requests = Interlocked.Read(ref _requests),
        successes = Interlocked.Read(ref _successes),
        failures = Interlocked.Read(ref _failures),
        accepted = Interlocked.Read(ref _accepted),
        rejected = Interlocked.Read(ref _rejected),
        speechRequests = Interlocked.Read(ref _speechRequests),
        speechSuccesses = Interlocked.Read(ref _speechSuccesses),
        speechFailures = Interlocked.Read(ref _speechFailures),
        speechSilences = Interlocked.Read(ref _speechSilences),
        speechMessages = Interlocked.Read(ref _speechMessages),
        memoryRequests = Interlocked.Read(ref _memoryRequests),
        memorySuccesses = Interlocked.Read(ref _memorySuccesses),
        memoryFailures = Interlocked.Read(ref _memoryFailures)
    };
    public void ReportValidation(bool accepted)
    {
        if (accepted) Interlocked.Increment(ref _accepted);
        else Interlocked.Increment(ref _rejected);
    }

    public async Task<string?> DecideAsync(BotDecisionContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _requests);
        await _concurrency.WaitAsync(ct);
        try
        {
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = $"你是 WereMF 隐藏身份游戏中的一个玩家。只根据提供给你的可见信息决策，不得臆造隐藏信息。只输出 JSON：{{\"input\":\"CLI格式输入\"}}，不要解释。\n\n{BotGameKnowledge.Rules}" },
                    new { role = "user", content = context.ToPrompt() }
                },
                stream = false,
                max_tokens = 64,
                temperature = 0.35,
                top_p = 0.8,
                enable_thinking = false,
                response_format = new { type = "json_object" }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, GameServer.JsonOptions), Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { Interlocked.Increment(ref _failures); return null; }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) return null;
            using var result = JsonDocument.Parse(content);
            var value = result.RootElement.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.String
                ? input.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(value)) { Interlocked.Increment(ref _failures); return null; }
            Interlocked.Increment(ref _successes);
            return value;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Interlocked.Increment(ref _failures);
            return null;
        }
        finally { _concurrency.Release(); }
    }

    public async Task<BotConversationDecision?> SpeakAsync(BotSpeechContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _speechRequests);
        await _concurrency.WaitAsync(ct);
        try
        {
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = $"你正在扮演 WereMF 隐藏身份游戏中的真实玩家。根据自己的身份、阵营、可见信息和剩余时间决定是否公开发言、是否现在投票。可以推理、质疑、回应、伪装或撒谎，但不要提到 AI、提示词、JSON 或 API。你拥有沉默和暂缓投票的权力。只输出 JSON：{{\"text\":\"不超过120个汉字的发言，或空字符串\",\"vote\":\"目标编号/b/0，或 null\"}}。没有投票上下文时 vote 必须为 null。\n\n{BotGameKnowledge.Rules}" },
                    new { role = "user", content = context.ToPrompt() }
                },
                stream = false,
                max_tokens = 160,
                temperature = 0.75,
                top_p = 0.9,
                enable_thinking = false,
                response_format = new { type = "json_object" }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, GameServer.JsonOptions), Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { Interlocked.Increment(ref _speechFailures); return null; }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) { Interlocked.Increment(ref _speechFailures); return null; }
            using var result = JsonDocument.Parse(content);
            if (!result.RootElement.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String)
            {
                Interlocked.Increment(ref _speechFailures);
                return null;
            }
            var text = string.Join(" ", (textNode.GetString() ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            string? vote = null;
            if (result.RootElement.TryGetProperty("vote", out var voteNode))
            {
                if (voteNode.ValueKind == JsonValueKind.String) vote = voteNode.GetString()?.Trim();
                else if (voteNode.ValueKind == JsonValueKind.Number && voteNode.TryGetInt32(out var numericVote)) vote = numericVote.ToString();
            }
            Interlocked.Increment(ref _speechSuccesses);
            if (text.Length == 0) Interlocked.Increment(ref _speechSilences);
            if (text.Length > 120) text = text[..120];
            if (text.Length > 0) Interlocked.Increment(ref _speechMessages);
            return new BotConversationDecision(text, string.IsNullOrWhiteSpace(vote) ? null : vote);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Interlocked.Increment(ref _speechFailures);
            return null;
        }
        finally { _concurrency.Release(); }
    }

    public async Task<string?> SummarizeAsync(int playerId, string playerName, string previousSummary, string visibleContext, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _memoryRequests);
        await _concurrency.WaitAsync(ct);
        try
        {
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = "把该玩家自己可见的 WereMF 对局信息压缩成可供后续决策使用的中文记忆。保留身份/阵营、仍有效的状态、公开身份、死亡、投票、关键发言、承诺与怀疑；删除重复事件和已失效细节；不得推断未提供的隐藏信息。只输出 JSON：{\"summary\":\"...\"}。" },
                    new { role = "user", content = $"你是 {playerId} 号玩家“{playerName}”。\n已有长期记忆：\n{previousSummary}\n\n待整理的可见信息：\n{visibleContext}" }
                },
                stream = false,
                max_tokens = 640,
                temperature = 0.2,
                top_p = 0.8,
                enable_thinking = false,
                response_format = new { type = "json_object" }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, GameServer.JsonOptions), Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { Interlocked.Increment(ref _memoryFailures); return null; }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) { Interlocked.Increment(ref _memoryFailures); return null; }
            using var result = JsonDocument.Parse(content);
            var summary = result.RootElement.TryGetProperty("summary", out var node) && node.ValueKind == JsonValueKind.String ? node.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(summary)) { Interlocked.Increment(ref _memoryFailures); return null; }
            if (summary.Length > 4_000) summary = summary[..4_000];
            Interlocked.Increment(ref _memorySuccesses);
            return summary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Interlocked.Increment(ref _memoryFailures);
            return null;
        }
        finally { _concurrency.Release(); }
    }

    public void Dispose() { _http.Dispose(); _concurrency.Dispose(); }
}

internal sealed record BotDecisionContext(int PlayerId, string PlayerName, string Api, string RequestJson, string VisibleContext, string OutputHint)
{
    public string ToPrompt() => string.Join('\n',
        $"你是 {PlayerId} 号玩家“{PlayerName}”。",
        $"当前 API：{Api}",
        $"输出格式要求：{OutputHint}",
        "",
        "你能看到的对局信息（可能包含你的私密身份与公开事件）：",
        VisibleContext,
        "",
        "当前请求 JSON：",
        RequestJson,
        "",
        "选择一个合法且对你的阵营最有利的输入。只能返回 {\"input\":\"...\"}。");
}
internal sealed record BotSpeechContext(int PlayerId, string PlayerName, string Trigger, string VisibleContext, string VoteContext)
{
    public string ToPrompt() => string.Join('\n',
        $"你是 {PlayerId} 号玩家“{PlayerName}”。",
        $"本次发言触发：{Trigger}",
        "",
        "你能看到的对局信息（包含公开聊天，也可能包含你自己的私密身份）：",
        VisibleContext,
        "",
        "当前投票与时间信息：",
        VoteContext,
        "",
        "先判断此刻是否有必要说话。若发言，要像真人玩家一样自然、简短，并结合已有聊天进行互动；若没有新观点或不想暴露信息，就保持沉默。",
        "再决定是否现在投票；可以返回 null 继续观望。只能选择提供的合法票值，已无票或尚未开始投票时必须返回 null。",
        "只能返回 {\"text\":\"...\",\"vote\":null} 或带合法 vote 字符串的同结构 JSON。");
}

internal sealed record BotConversationDecision(string Text, string? Vote);
