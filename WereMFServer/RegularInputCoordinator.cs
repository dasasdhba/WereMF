using System.Text.Json;

namespace WereMFServer;

internal sealed class RegularInputCoordinator
{
    public string? ExpectedTarget { get; private set; }
    public JsonElement? Prompt { get; private set; }
    public string? Api { get; private set; }
    public DateTimeOffset Deadline { get; private set; }

    public bool IsWaitingFor(string target) => ExpectedTarget == target;

    public void SetExpectedTarget(string target) => ExpectedTarget = target;
    public void SetPrompt(JsonElement? prompt) => Prompt = prompt;
    public void SetApi(string? api) => Api = api;
    public void SetDeadline(DateTimeOffset deadline) => Deadline = deadline;

    public void Begin(string target, JsonElement prompt, string api, DateTimeOffset deadline)
    {
        ExpectedTarget = target;
        Prompt = prompt;
        Api = api;
        Deadline = deadline;
    }

    public void Clear()
    {
        ExpectedTarget = null;
        Prompt = null;
        Api = null;
        Deadline = default;
    }
}
