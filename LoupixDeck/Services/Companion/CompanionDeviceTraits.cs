using LoupixDeck.Localization;
using LoupixDeck.Models;

namespace LoupixDeck.Services.Companion;

/// <summary>What a companion's config tells about its hardware when the device may be unplugged.</summary>
public static class CompanionDeviceTraits
{
    /// <summary>A device with side strips pages its dial columns separately; its config then holds
    /// left and right rotary pages.</summary>
    public static bool HasSideStrips(LoupedeckConfig config) =>
        config?.Profiles?.Any(p => p.Workspaces?.Any(w => w.LeftRotaryButtonPages?.Count > 0) == true) == true;

    /// <summary>The companion's mirror of the workspace with <paramref name="workspaceId"/>, or null.</summary>
    public static Workspace FindWorkspace(LoupedeckConfig config, Guid workspaceId) =>
        config?.Profiles?
            .Where(p => p.Workspaces != null)
            .SelectMany(p => p.Workspaces)
            .FirstOrDefault(w => w.Id == workspaceId);

    /// <summary>"Touch page 2: Scenes" for the page at 0-based <paramref name="index"/>, from the
    /// <paramref name="labelKey"/> format ("Touch page {0}") and the page's optional name.</summary>
    public static string PageLabel(string labelKey, int index, string pageName)
    {
        string label = Loc.Tr(labelKey, index + 1);
        return string.IsNullOrWhiteSpace(pageName) ? label : $"{label}: {pageName}";
    }
}
