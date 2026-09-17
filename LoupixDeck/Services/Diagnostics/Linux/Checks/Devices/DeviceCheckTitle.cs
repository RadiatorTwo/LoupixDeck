namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Titles for the per-device checks. Two identical decks produce two sets of the same checks,
/// so the unit has to be part of the title - otherwise the check column shows the same row
/// twice with no way to tell which deck it is about.
/// </summary>
internal static class DeviceCheckTitle
{
    /// <summary>The localized check title, followed by the unit it is about.</summary>
    public static string For(string id, LinuxDeckDevice device)
        => $"{DiagnosticCheckTitles.For(id)} — {device.Label}";
}
