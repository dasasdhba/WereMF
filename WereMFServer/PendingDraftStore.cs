namespace WereMFServer;

internal sealed class PendingDraftStore
{
    public void Save(PlayerSession player, string? skillId, string? api, string value, bool preSubmit)
    {
        var key = !string.IsNullOrWhiteSpace(skillId) ? $"skill:{skillId.Trim()}" : !string.IsNullOrWhiteSpace(api) ? $"api:{api.Trim()}" : "";
        if (key.Length is 0 or > 100 || value.Length > 240) throw new ClientVisibleException("预选草稿无效");
        lock (player.Drafts)
        {
            if (player.Drafts.Count >= 64 && !player.Drafts.ContainsKey(key)) player.Drafts.Remove(player.Drafts.Keys.First());
            player.Drafts[key] = new DraftEntry(value, preSubmit);
        }
    }

    public DraftEntry? Find(PlayerSession player, string skillId, string api)
    {
        lock (player.Drafts)
        {
            if (player.Drafts.TryGetValue($"skill:{skillId}", out var skillDraft) && !string.IsNullOrWhiteSpace(skillDraft.Value)) return skillDraft;
            if (player.Drafts.TryGetValue($"api:{api}", out var apiDraft) && !string.IsNullOrWhiteSpace(apiDraft.Value)) return apiDraft;
            return null;
        }
    }

    public DraftEntry? TakePreSubmit(PlayerSession player, string? skillId, string api)
    {
        var keys = skillId is null ? new[] { $"api:{api}" } : new[] { $"skill:{skillId}", $"api:{api}" };
        lock (player.Drafts)
        {
            foreach (var key in keys)
            {
                if (!player.Drafts.TryGetValue(key, out var draft) || !draft.PreSubmit) continue;
                player.Drafts[key] = draft with { PreSubmit = false };
                return draft;
            }
        }
        return null;
    }

    public bool RemovePreSubmit(PlayerSession player, string skillId)
    {
        lock (player.Drafts)
            return player.Drafts.Remove($"skill:{skillId}", out var draft) && draft.PreSubmit;
    }
}
