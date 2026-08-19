using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WereMFServer;

internal sealed class LlmBotClient : IDisposable
{
    private const int DefaultCircuitFailureThreshold = 2;
    private const int DefaultCircuitBreakSeconds = 60;
    private static readonly object TraceGate = new();
    private static readonly string TracePath = Path.GetFullPath("llm_requests.log");
    private static long NextTraceId;
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly LlmBotClient? _fallback;
    private readonly int _circuitFailureThreshold;
    private readonly long _circuitBreakMilliseconds;
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
    private long _timeoutFailures;
    private long _transportFailures;
    private long _httpStatusFailures;
    private long _invalidResponseFailures;
    private int _consecutiveFailures;
    private long _circuitOpenUntilUnixMs;
    private int _circuitProbeInFlight;
    private long _circuitSkipped;
    private long _fallbackAttempts;
    private long _fallbackSuccesses;

    public LlmBotClient(string endpoint, string? apiKey, string model, int timeoutSeconds, int maxTokens, LlmBotClient? fallback = null, int circuitFailureThreshold = DefaultCircuitFailureThreshold, int circuitBreakSeconds = DefaultCircuitBreakSeconds)
    {
        var baseUri = new Uri(endpoint);
        _endpoint = baseUri.GetLeftPart(UriPartial.Path);
        _model = model;
        _maxTokens = maxTokens;
        _fallback = fallback;
        _circuitFailureThreshold = Math.Max(1, circuitFailureThreshold);
        _circuitBreakMilliseconds = Math.Max(1, circuitBreakSeconds) * 1_000L;
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(apiKey)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private long LogRequest(string operation, string systemPrompt, string userPrompt)
    {
        var traceId = Interlocked.Increment(ref NextTraceId);
        WriteTrace(
            $"[LLM_TRACE {traceId} REQUEST utc={DateTimeOffset.UtcNow:O} operation={operation} model={_model} endpoint={_endpoint}]",
            "[LLM_TRACE SYSTEM_PROMPT_BEGIN]",
            systemPrompt,
            "[LLM_TRACE SYSTEM_PROMPT_END]",
            "[LLM_TRACE USER_PROMPT_BEGIN]",
            userPrompt,
            "[LLM_TRACE USER_PROMPT_END]",
            $"[LLM_TRACE {traceId} REQUEST_END]");
        return traceId;
    }

    private void LogResponse(long traceId, HttpStatusCode statusCode, string? answer, int responseLength) =>
        WriteTrace(
            $"[LLM_TRACE {traceId} RESPONSE utc={DateTimeOffset.UtcNow:O} status={(int)statusCode} {statusCode}]",
            answer ?? $"<model content unavailable; response_length={responseLength}>",
            $"[LLM_TRACE {traceId} RESPONSE_END]");

    private void LogFailure(long traceId, Exception error) =>
        WriteTrace(
            $"[LLM_TRACE {traceId} FAILURE utc={DateTimeOffset.UtcNow:O} type={error.GetType().Name}]",
            error.Message,
            $"[LLM_TRACE {traceId} FAILURE_END]");

    private void LogSkipped(string operation) =>
        WriteTrace($"[LLM_TRACE SKIPPED utc={DateTimeOffset.UtcNow:O} operation={operation} model={_model} endpoint={_endpoint} reason=circuit_open]");

    private static void WriteTrace(params string[] lines)
    {
        lock (TraceGate)
        {
            try
            {
                File.AppendAllText(
                    TracePath,
                    string.Join(Environment.NewLine, lines) + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (IOException)
            {
                // Diagnostics must never affect model calls or game progress.
            }
            catch (UnauthorizedAccessException)
            {
                // Diagnostics must never affect model calls or game progress.
            }
        }
    }

    public object Stats => BuildStats(null);

    public object StatsWithConversationMetrics(BotConversationMetrics conversationMetrics) => BuildStats(conversationMetrics);

    private object BuildStats(BotConversationMetrics? conversationMetrics) => new
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
        timeoutFailures = Interlocked.Read(ref _timeoutFailures),
        transportFailures = Interlocked.Read(ref _transportFailures),
        httpStatusFailures = Interlocked.Read(ref _httpStatusFailures),
        invalidResponseFailures = Interlocked.Read(ref _invalidResponseFailures),
        consecutiveFailures = Volatile.Read(ref _consecutiveFailures),
        circuitOpen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Interlocked.Read(ref _circuitOpenUntilUnixMs),
        circuitOpenUntilUtc = CircuitOpenUntilUtc(),
        circuitProbeInFlight = Volatile.Read(ref _circuitProbeInFlight) != 0,
        circuitSkipped = Interlocked.Read(ref _circuitSkipped),
        circuitFailureThreshold = _circuitFailureThreshold,
        circuitBreakSeconds = _circuitBreakMilliseconds / 1_000,
        fallbackAttempts = Interlocked.Read(ref _fallbackAttempts),
        fallbackSuccesses = Interlocked.Read(ref _fallbackSuccesses),
        fallbackStats = _fallback?.Stats,
        conversationStats = conversationMetrics?.Snapshot
    };
    public void ReportValidation(bool accepted)
    {
        if (accepted) Interlocked.Increment(ref _accepted);
        else Interlocked.Increment(ref _rejected);
    }

    private void ClassifyFailure(Exception error)
    {
        if (error is TaskCanceledException) Interlocked.Increment(ref _timeoutFailures);
        else if (error is HttpRequestException or IOException) Interlocked.Increment(ref _transportFailures);
        else Interlocked.Increment(ref _invalidResponseFailures);
        MarkModelFailure();
    }

    private string? CircuitOpenUntilUtc()
    {
        var value = Interlocked.Read(ref _circuitOpenUntilUnixMs);
        return value > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime.ToString("O") : null;
    }

    private bool TryEnterModelRequest()
    {
        var openUntil = Interlocked.Read(ref _circuitOpenUntilUnixMs);
        if (openUntil <= 0) return true;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now < openUntil)
        {
            Interlocked.Increment(ref _circuitSkipped);
            return false;
        }
        if (Interlocked.CompareExchange(ref _circuitProbeInFlight, 1, 0) == 0) return true;
        Interlocked.Increment(ref _circuitSkipped);
        return false;
    }

    private void MarkModelSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _circuitOpenUntilUnixMs, 0);
        Interlocked.Exchange(ref _circuitProbeInFlight, 0);
    }

    private void MarkModelFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (failures >= _circuitFailureThreshold || Interlocked.Read(ref _circuitOpenUntilUnixMs) > 0)
            Interlocked.Exchange(ref _circuitOpenUntilUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _circuitBreakMilliseconds);
        Interlocked.Exchange(ref _circuitProbeInFlight, 0);
    }

    private void ReportHttpStatusFailure()
    {
        Interlocked.Increment(ref _httpStatusFailures);
        MarkModelFailure();
    }

    private void ReportInvalidResponseFailure()
    {
        Interlocked.Increment(ref _invalidResponseFailures);
        MarkModelFailure();
    }

    private void ReleaseCircuitProbe() => Interlocked.Exchange(ref _circuitProbeInFlight, 0);

    private static JsonDocument ParseModelJson(string content)
    {
        var trimmed = content.Trim();
        try { return JsonDocument.Parse(trimmed); }
        catch (JsonException)
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start) return JsonDocument.Parse(trimmed[start..(end + 1)]);
            throw;
        }
    }

    private async Task<T?> RaceWithFallbackAsync<T>(
        Func<CancellationToken, Task<T?>> primary,
        Func<CancellationToken, Task<T?>> fallback,
        CancellationToken ct) where T : class
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Interlocked.Increment(ref _fallbackAttempts);
        var primaryTask = primary(linked.Token);
        var fallbackTask = fallback(linked.Token);
        try
        {
            var first = await Task.WhenAny(primaryTask, fallbackTask);
            var firstResult = await first;
            if (firstResult is not null)
            {
                if (ReferenceEquals(first, fallbackTask)) Interlocked.Increment(ref _fallbackSuccesses);
                linked.Cancel();
                return firstResult;
            }

            var second = ReferenceEquals(first, primaryTask) ? fallbackTask : primaryTask;
            var secondResult = await second;
            if (secondResult is not null && ReferenceEquals(second, fallbackTask))
                Interlocked.Increment(ref _fallbackSuccesses);
            if (secondResult is not null) linked.Cancel();
            return secondResult;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }
    public async Task<BotModelDecision?> DecideAsync(BotDecisionContext context, CancellationToken ct = default)
    {
        if (_fallback is null) return await DecidePrimaryAsync(context, ct);
        return await RaceWithFallbackAsync(
            token => DecidePrimaryAsync(context, token),
            token => _fallback.DecideAsync(context, token),
            ct);
    }

    private async Task<BotModelDecision?> DecidePrimaryAsync(BotDecisionContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _requests);
        await _concurrency.WaitAsync(ct);
        var traceId = 0L;
        try
        {
            if (!TryEnterModelRequest()) { Interlocked.Increment(ref _failures); LogSkipped("decision"); return null; }
            var systemPrompt = $"你是 WereMF 隐藏身份游戏中的一个玩家。只根据提供给你的当前权威状态、可见历史和完整规则决策，不得臆造隐藏信息。当前权威状态覆盖一切旧状态。每次反应后都先检查一次 memory_add：如果本次做过自己的可核对行动，或对外说过以后需要保持一致/可能被追问的话，或根据玩家公开发言形成了尚未被权威状态证实且以后可能有用的推测，就优先记录一条短记忆；技能或并发选择中如果你实际选择了非 0 行动，默认记录这次自己的行动；只有上述情况都没有时才填 null。记忆是便签，不是当前局面总结。可以记录‘我公开声称自己是炮仙，后续要保持说法一致’、‘我承诺今晚不再使用技能’、‘我根据3号的公开发言暂时怀疑他在伪装’；不要记录当前状态、资源、存活、投票、临时效果或自己的身份本身。不要把推测写成事实。必须引用实际可见事件编号，最多一条，格式为 {{\"text\":\"...\",\"kind\":\"own_action|public_claim|inference|promise\",\"source_events\":[数字]}}。只输出 JSON：{{\"input\":\"CLI格式输入\",\"memory_add\":null}}，不要解释。\n\n【完整规则】\n{BotGameKnowledge.Rules}\n\n【行为策略】\n{BotGameKnowledge.Strategy}\n\n【当前决策规则焦点】\n{context.RuleFocus}";
            var userPrompt = context.ToPrompt();
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                max_tokens = Math.Max(64, _maxTokens),
                temperature = 0.35,
                top_p = 0.8,
                enable_thinking = false,
                response_format = new { type = "json_object" }
            };
            var requestJson = JsonSerializer.Serialize(body, GameServer.JsonOptions);
            traceId = LogRequest("decision", systemPrompt, userPrompt);
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) { LogResponse(traceId, response.StatusCode, null, responseBody.Length); Interlocked.Increment(ref _failures); ReportHttpStatusFailure(); return null; }
            using var json = JsonDocument.Parse(responseBody);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            LogResponse(traceId, response.StatusCode, content, responseBody.Length);
            if (string.IsNullOrWhiteSpace(content)) { Interlocked.Increment(ref _failures); ReportInvalidResponseFailure(); return null; }
            using var result = ParseModelJson(content);
            var value = result.RootElement.TryGetProperty("input", out var input)
                ? input.ValueKind == JsonValueKind.String ? input.GetString()?.Trim()
                : input.ValueKind == JsonValueKind.Number ? input.GetRawText()
                : null
                : null;
            if (string.IsNullOrWhiteSpace(value)) { Interlocked.Increment(ref _failures); ReportInvalidResponseFailure(); return null; }
            MarkModelSuccess();
            Interlocked.Increment(ref _successes);
            return new BotModelDecision(value, ParseMemoryCandidate(result.RootElement));
        }
        catch (OperationCanceledException e) when (ct.IsCancellationRequested) { ReleaseCircuitProbe(); LogFailure(traceId, e); return null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Interlocked.Increment(ref _failures);
            LogFailure(traceId, e);
            ClassifyFailure(e);
            return null;
        }
        finally { _concurrency.Release(); }
    }

    public async Task<BotConversationDecision?> SpeakAsync(BotSpeechContext context, CancellationToken ct = default)
    {
        if (_fallback is null) return await SpeakPrimaryAsync(context, ct);
        return await RaceWithFallbackAsync(
            token => SpeakPrimaryAsync(context, token),
            token => _fallback.SpeakAsync(context, token),
            ct);
    }

    private async Task<BotConversationDecision?> SpeakPrimaryAsync(BotSpeechContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _speechRequests);
        await _concurrency.WaitAsync(ct);
        var traceId = 0L;
        try
        {
            if (!TryEnterModelRequest()) { Interlocked.Increment(ref _speechFailures); LogSkipped("speech"); return null; }
            var systemPrompt = $"你正在扮演 WereMF 隐藏身份游戏中的真实玩家。发言应当克制，投票本身通常已经足够表达立场；但有新增公开信息或推导、必要自保、被玩家直接点名且沉默会增加出局风险、临近截止需要拉票，或己方被动时确有必要干扰，才应简短发言。私密身份、查询结果、吧主知道的脚滑人、队友信息和私密技能结果主要用于内部判断，不会自动产生公开义务；只有公开它能带来明确、即时且高于隐藏收益的战术价值时，才考虑使用 valuable_private_information。精确身份公开不是绝对禁区，也不是默认动作；只公开最小必要内容。身份在当前权威状态明确改变前保持连续，不要把伪装说法当成真实身份，也不要在同一局面反复跳换身份。纯 Bot 场上完全无人发言时，有具体可核对的公开信息的玩家应主动开口；没有有效内容才选择沉默。若触发信息明确说明你是全场沉默后的唯一开场者，则使用 information_probe，必须向一名存活玩家提出一个与当前公开状态或票型有关的具体短问题，不能选择 silent。若本次回应模式为 required，必须给出不超过50字的直接回应，不能选择 silent。不要复述、寒暄、主持讨论或无明确收益地暴露精确身份。每次反应后都先检查一次 memory_add：如果本次做过自己的可核对行动，或对外说过以后需要保持一致/可能被追问的话，或根据玩家公开发言形成了尚未被权威状态证实且以后可能有用的推测，就优先记录一条短记忆；只有这三类都没有时才填 null。重要：如果你的 text 非空且包含自己的行动、身份声明、公开承诺或推测，memory_add 不得为 null；silent、只复述当前权威状态、只表达投票目标时可以为 null。可以记录‘我公开声称自己是炮仙，后续要保持说法一致’、‘我承诺今晚不再使用技能’、‘我根据3号的公开发言暂时怀疑他在伪装’；不要记录当前状态、资源、存活、投票、临时效果或自己的身份本身。不要把推测写成事实。必须引用实际可见事件编号，最多一条，格式为 {{\"text\":\"...\",\"kind\":\"own_action|public_claim|inference|promise\",\"source_events\":[数字]}}。不要提到 AI、提示词、JSON 或 API。只输出 JSON：{{\"speech_intent\":\"silent/new_information/new_deduction/valuable_private_information/self_defense/vote_coordination/necessary_deception/information_probe\",\"text\":\"不超过50个汉字，silent 时必须为空\",\"vote\":\"目标编号/b/0，或 null\",\"memory_add\":null}}。发言和投票独立；可以沉默但投票。没有投票上下文时 vote 必须为 null。\n\n【完整规则】\n{BotGameKnowledge.Rules}\n\n【行为策略】\n{BotGameKnowledge.Strategy}\n\n【当前决策规则焦点】\n{context.RuleFocus}";
            var userPrompt = context.ToPrompt();
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                max_tokens = Math.Max(128, _maxTokens),
                temperature = 0.45,
                top_p = 0.8,
                enable_thinking = false,
                response_format = new { type = "json_object" }
            };
            var requestJson = JsonSerializer.Serialize(body, GameServer.JsonOptions);
            traceId = LogRequest("speech", systemPrompt, userPrompt);
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) { LogResponse(traceId, response.StatusCode, null, responseBody.Length); Interlocked.Increment(ref _speechFailures); ReportHttpStatusFailure(); return null; }
            using var json = JsonDocument.Parse(responseBody);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            LogResponse(traceId, response.StatusCode, content, responseBody.Length);
            if (string.IsNullOrWhiteSpace(content)) { Interlocked.Increment(ref _speechFailures); ReportInvalidResponseFailure(); return null; }
            using var result = ParseModelJson(content);
            var rawText = result.RootElement.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String
                ? textNode.GetString() ?? ""
                : "";
            var text = string.Join(" ", rawText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var intent = result.RootElement.TryGetProperty("speech_intent", out var intentNode) && intentNode.ValueKind == JsonValueKind.String
                ? intentNode.GetString()
                : "unspecified";
            if (string.Equals(intent, "silent", StringComparison.OrdinalIgnoreCase)) text = "";
            string? vote = null;
            if (result.RootElement.TryGetProperty("vote", out var voteNode))
            {
                if (voteNode.ValueKind == JsonValueKind.String) vote = voteNode.GetString()?.Trim();
                else if (voteNode.ValueKind == JsonValueKind.Number && voteNode.TryGetInt32(out var numericVote)) vote = numericVote.ToString();
            }
            MarkModelSuccess();
            Interlocked.Increment(ref _speechSuccesses);
            if (text.Length == 0) Interlocked.Increment(ref _speechSilences);
            if (text.Length > 50) text = text[..50];
            if (text.Length > 0) Interlocked.Increment(ref _speechMessages);
            return new BotConversationDecision(text, string.IsNullOrWhiteSpace(vote) ? null : vote, -1, intent ?? "unspecified", ParseMemoryCandidate(result.RootElement));
        }
        catch (OperationCanceledException e) when (ct.IsCancellationRequested) { ReleaseCircuitProbe(); LogFailure(traceId, e); return null; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            Interlocked.Increment(ref _speechFailures);
            LogFailure(traceId, e);
            ClassifyFailure(e);
            return null;
        }
        finally { _concurrency.Release(); }
    }

    private static BotMemoryCandidate? ParseMemoryCandidate(JsonElement root)
    {
        if (!root.TryGetProperty("memory_add", out var node) || node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String)
            return null;

        var text = textNode.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        var kind = node.TryGetProperty("kind", out var kindNode) && kindNode.ValueKind == JsonValueKind.String
            ? kindNode.GetString()?.Trim() ?? "inference"
            : "inference";
        var sources = new List<long>();
        if (node.TryGetProperty("source_events", out var sourceNode) && sourceNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sourceNode.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number)) sources.Add(number);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var parsed)) sources.Add(parsed);
            }
        }
        return new BotMemoryCandidate(text, kind, sources.Distinct().Take(4).ToArray());
    }

    public void Dispose() { _fallback?.Dispose(); _http.Dispose(); _concurrency.Dispose(); }
}

internal sealed record BotDecisionContext(int PlayerId, string PlayerName, string Api, string RequestJson, string VisibleContext, string OutputHint, string RuleFocus = "")
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
        "选择一个合法且对你的阵营最有利的输入。只能返回 {\"input\":\"...\",\"memory_add\":null}；如需记录记忆，memory_add 必须包含 text、kind、source_events。");
}
internal sealed record BotSpeechContext(int PlayerId, string PlayerName, string Trigger, string VisibleContext, string VoteContext, string RuleFocus = "", BotSpeechResponseMode ResponseMode = BotSpeechResponseMode.Optional)
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
        $"回应模式：{ResponseMode}。",
        "先判断是否有实际信息、推导、明确被推出风险下的必要自保、被点名回应、拉票或必要干扰价值；有则简短发言，无则选择 silent。投票本身已经表达立场，不能仅因为自己投票或收到少量票就解释。",
        "私密身份、查询结果和私密技能信息主要用于内部判断，不自动要求公开。只有公开某个私密结论的即时战术收益明显高于隐藏身份收益时，才使用 valuable_private_information；它是可选意图，silent 仍然有效。身份公开不是绝对禁区，但必须有明确局势理由，只说最小必要内容。吧主知道脚滑人或脚滑人查到身份时，默认不要主动供出对象或信息来源。当前权威状态没有明确身份变化时，保持真实身份连续；伪装身份也不要在同一局面反复跳换。直接点名/required 模式必须短回应。纯 Bot 全体沉默后的唯一开场者必须以 information_probe 向一名存活玩家提出具体短问题，不得 silent。极简表达，不复述，禁止“先看票型”之类无信息句。",
        "再决定是否现在投票：投票阶段有合法非零目标时通常应立即投一票。第一票尚未使用且场上连续一个思考间隔无人发言、无人投票时，即使没有充分依据也应随机选择合法玩家，避免半数弃票风险；预算不超过 60 秒或只剩最后一次机会时也不得继续观望。只能选择提供的合法票值，已无票或尚未开始投票时必须返回 null。",
        "memory_add 先检查三类候选：自己的可核对行动、以后需要保持一致或可能被追问的公开说法、基于公开发言形成的未证实推测。技能或并发选择非 0 时默认记录自己的行动；发言 text 非空且包含自己的行动、身份声明、承诺或推测时不得为 null。只有沉默、单纯复述当前状态或只表达投票目标时才可以为 null。不要记录当前状态、资源、存活、投票、临时效果或自己的身份本身。必须引用实际可见事件编号，格式为 {\"text\":\"...\",\"kind\":\"own_action|public_claim|inference|promise\",\"source_events\":[数字]}。",
        "只能返回包含 speech_intent、text、vote、memory_add 的指定 JSON。silent 时 text 必须为空，但 vote 仍可合法投票。");
}

internal sealed record BotConversationDecision(string Text, string? Vote, long StateVersion = -1, string Intent = "unspecified", BotMemoryCandidate? Memory = null);
