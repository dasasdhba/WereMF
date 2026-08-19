using System.Security.Cryptography;

namespace WereMFServer;

internal enum BotSpeechResponseMode
{
    Optional,
    Required,
    RequiredInformationProbe
}

internal sealed record BotSpeechCandidate<T>(T Bot, BotConversationDecision? Decision, BotSpeechResponseMode ResponseMode);

// This only classifies delivery policy. It never interprets game state or private role data.
internal static class BotConversationPolicy
{
    public static string RandomSafeVoteChoice(int playerId, IReadOnlyCollection<string> choices)
    {
        var safeChoices = choices
            .Where(choice => choice != "b" &&
                            (!int.TryParse(choice, out var targetId) || targetId != playerId))
            .ToArray();
        return safeChoices.Length == 0
            ? "0"
            : safeChoices[RandomNumberGenerator.GetInt32(safeChoices.Length)];
    }

    public static BotSpeechResponseMode ResponseModeFor(string trigger, string botName) =>
        trigger.Contains(botName, StringComparison.OrdinalIgnoreCase)
            ? BotSpeechResponseMode.Required
            : BotSpeechResponseMode.Optional;

    public static int? SelectOpeningFallbackSpeaker<T>(IReadOnlyList<T> eligibleBots, IReadOnlyCollection<BotConversationDecision?> decisions)
    {
        if (eligibleBots.Count == 0 || decisions.Any(x => !string.IsNullOrWhiteSpace(x?.Text))) return null;
        return Random.Shared.Next(eligibleBots.Count);
    }

    // Completion order is retained within each priority group.  A directly named Bot must not
    // lose its required reply merely because optional model calls finished sooner.
    public static IReadOnlyList<BotSpeechCandidate<T>> SelectSpeechCandidates<T>(
        IEnumerable<BotSpeechCandidate<T>> completed,
        int speechLimit)
    {
        if (speechLimit <= 0) return [];
        var candidates = completed.Where(x => !string.IsNullOrWhiteSpace(x.Decision?.Text));
        return candidates.Where(x => x.ResponseMode != BotSpeechResponseMode.Optional)
            .Concat(candidates.Where(x => x.ResponseMode == BotSpeechResponseMode.Optional))
            .Take(speechLimit)
            .ToArray();
    }

}
