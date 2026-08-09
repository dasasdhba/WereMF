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
        Func<JsonElement, string, string, bool> isLegal)
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
        var accepted = !string.IsNullOrWhiteSpace(candidate) && isLegal(request, api, candidate);
        client.ReportValidation(accepted);
        return accepted ? candidate! : fallback;
    }
}
