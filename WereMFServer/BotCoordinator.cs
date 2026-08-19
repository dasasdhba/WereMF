using System.Text.Json;

namespace WereMFServer;

internal sealed class BotCoordinator(LlmBotClient? client)
{
    public async Task<string> DecideInputAsync(
        PlayerSession player,
        JsonElement request,
        string api,
        string fallback,
        Func<Task<string>> visibleContext,
        Func<string> ruleFocus,
        Func<JsonElement, string, string, bool> isLegal,
        Action<BotMemoryCandidate?> remember)
    {
        if (client is null) return fallback;
        var context = new BotDecisionContext(
            player.GameId,
            player.Name,
            api,
            request.GetRawText(),
            await visibleContext(),
            "严格使用当前请求描述的 CLI 格式；0 表示放弃（若允许）",
            ruleFocus());
        var candidate = await client.DecideAsync(context);
        var input = candidate?.Input;
        var accepted = !string.IsNullOrWhiteSpace(input) && isLegal(request, api, input);
        client.ReportValidation(accepted);
        remember(candidate?.Memory);
        return accepted ? input! : fallback;
    }
}
