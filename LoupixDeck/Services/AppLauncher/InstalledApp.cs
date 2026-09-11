using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>Where a discovered application was found. Drives grouping in the picker and, on
/// Windows, how the target is shaped.</summary>
public enum AppSource
{
    /// <summary>A Start Menu shortcut, or a <c>.desktop</c> entry on Linux.</summary>
    Installed,

    Steam,

    Epic,

    /// <summary>A Microsoft Store (packaged) application.</summary>
    Store
}

/// <summary>
/// One launchable application found on this machine. Purely a discovery result: nothing here is
/// persisted, so a rescan is always free to produce different values.
/// </summary>
public sealed class InstalledApp
{
    /// <summary>Display name, as the system's own application menu shows it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// What actually launches the application, handed to <c>System.LaunchApp</c>: an executable
    /// path, a launcher URI, a <c>shell:AppsFolder\…</c> target, or a <c>.desktop</c> path.
    /// </summary>
    public required string Target { get; init; }

    public AppSource Source { get; init; }

    /// <summary>True when the entry is a game, which the picker sorts first.</summary>
    public bool IsGame { get; init; }

    /// <summary>An image the platform already provides (Steam library art, a Store logo, a
    /// <c>.desktop</c> icon resolved to a file). Null when an icon has to be extracted.</summary>
    public string PreResolvedIcon { get; init; }

    /// <summary>
    /// File to extract an icon from when <see cref="PreResolvedIcon"/> is null — a shortcut's
    /// resolved target, or a game's executable. Null when there is nothing to extract from.
    /// </summary>
    public string IconExtractSource { get; init; }

    /// <summary>
    /// Identity for de-duplication: the launch target, case-folded. Deliberately not the display
    /// name — two unrelated shortcuts can share a name, and the same program reached two ways is
    /// still the same program.
    /// </summary>
    public string Identity => Target?.ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Process name in the form context rules use — the bare executable name with no path and no
    /// <c>.exe</c> suffix, matching <c>ActiveWindowInfo.ProcessName</c>. Empty when the app is only
    /// reachable through a URI, where the eventual process cannot be known up front.
    /// </summary>
    public string ProcessName
    {
        get
        {
            string executable = IconExtractSource;
            if (string.IsNullOrWhiteSpace(executable))
            {
                executable = Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Target : null;
            }

            if (string.IsNullOrWhiteSpace(executable))
                return string.Empty;

            try
            {
                return ContextRuleMatcher.Normalize(Path.GetFileName(executable));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Short label shown under the name in the picker.</summary>
    public string SourceLabel => Source switch
    {
        AppSource.Steam => "Steam",
        AppSource.Epic => "Epic Games",
        AppSource.Store => "Microsoft Store",
        _ => "Installed"
    };
}