namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Installation;

/// <summary>
/// The paths install-loupixdeck.sh writes to, in both of its modes: system-wide under /usr, and
/// user-scoped under ~/.local when /usr is read-only (SteamOS and the other atomic distributions).
/// The checks read the same two sets so a user installation is not reported as a missing one.
/// </summary>
internal static class InstallationPaths
{
    /// <summary>The desktop entry, system first.</summary>
    public static IEnumerable<string> DesktopEntries()
    {
        yield return "/usr/share/applications/loupixdeck.desktop";

        string home = Home();

        if (home != null)
        {
            yield return Path.Combine(home, ".local/share/applications/loupixdeck.desktop");
        }
    }

    /// <summary>The launcher symlink the installer creates.</summary>
    public static IEnumerable<string> Launchers()
    {
        yield return "/usr/local/bin/loupixdeck";

        string home = Home();

        if (home != null)
        {
            yield return Path.Combine(home, ".local/bin/loupixdeck");
        }
    }

    /// <summary>The install directories, which is where a duplicate old copy would sit.</summary>
    public static IEnumerable<string> InstallDirectories()
    {
        yield return "/usr/local/lib/loupixdeck";

        string home = Home();

        if (home != null)
        {
            yield return Path.Combine(home, ".local/lib/loupixdeck");
        }
    }

    /// <summary>The XDG autostart entry, which only ever lives in the user's home.</summary>
    public static string AutostartEntry()
    {
        string home = Home();

        return home == null ? null : Path.Combine(home, ".config/autostart/loupixdeck.desktop");
    }

    /// <summary>The keep-list file that saves the udev rule across an atomic system update.</summary>
    public const string AtomicKeepFile = "/etc/atomic-update.conf.d/loupixdeck.conf";

    private static string Home()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrEmpty(home) ? null : home;
    }
}
