using System.Runtime.InteropServices;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>Where this build of LoupixDeck is running from.</summary>
public enum InstallMode
{
    /// <summary>The location was not recognized.</summary>
    Unknown,

    /// <summary>Installed system-wide, below /usr.</summary>
    System,

    /// <summary>Installed into the user's home directory.</summary>
    User,

    /// <summary>A user installation on SteamOS, where /usr is read-only.</summary>
    SteamOs,

    /// <summary>Started from a build output rather than from an installation.</summary>
    Source
}

/// <summary>
/// The single place the diagnostics read system facts from. Checks never touch environment
/// variables or /etc themselves, which keeps both the parsing and - more importantly - the set
/// of values that can reach a copied report in one reviewable file.
///
/// Nothing here enumerates the environment: every variable is read by name.
/// </summary>
internal static class LinuxSystemFacts
{
    /// <summary>
    /// Parses an os-release file into its key/value pairs. Returns an empty dictionary when
    /// neither the /etc nor the /usr/lib variant can be read.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadOsRelease()
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string path in new[] { "/etc/os-release", "/usr/lib/os-release" })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    int separator = trimmed.IndexOf('=');

                    if ((separator <= 0) || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    string key = trimmed[..separator].Trim();
                    string value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');

                    values.TryAdd(key, value);
                }

                if (values.Count > 0)
                {
                    return values;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Diagnostics] Could not read {path}: {ex.Message}");
            }
        }

        return values;
    }

    /// <summary>The running kernel release, read from /proc rather than by spawning uname.</summary>
    public static string KernelRelease()
    {
        try
        {
            return File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Could not read the kernel release: {ex.Message}");
            return null;
        }
    }

    /// <summary>The effective user id, read from /proc/self/status.</summary>
    public static int? EffectiveUserId()
    {
        try
        {
            foreach (string line in File.ReadAllLines("/proc/self/status"))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] fields = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries |
                                                       StringSplitOptions.TrimEntries);

                // real, effective, saved, filesystem
                if ((fields.Length >= 2) && int.TryParse(fields[1], out int effective))
                {
                    return effective;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Could not read the effective user id: {ex.Message}");
        }

        return null;
    }

    /// <summary>The desktop environment, from the first XDG variable that carries one.</summary>
    public static string DesktopEnvironment()
        => FirstNonEmpty("XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "DESKTOP_SESSION");

    /// <summary>
    /// The session type. Falls back to inferring it from WAYLAND_DISPLAY/DISPLAY when
    /// XDG_SESSION_TYPE is absent, which happens in a systemd user unit and over SSH.
    /// </summary>
    public static string SessionType()
    {
        string declared = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");

        if (!string.IsNullOrWhiteSpace(declared))
        {
            return declared.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(WaylandDisplay()))
        {
            return "wayland";
        }

        return string.IsNullOrWhiteSpace(Display()) ? null : "x11";
    }

    /// <summary>The X11 display, the value LinuxActiveWindowMonitor gates on.</summary>
    public static string Display() => Environment.GetEnvironmentVariable("DISPLAY");

    /// <summary>The Wayland display socket name.</summary>
    public static string WaylandDisplay() => Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

    /// <summary>The XDG runtime directory, falling back to /run/user/&lt;uid&gt;.</summary>
    public static string RuntimeDirectory()
    {
        string declared = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

        if (!string.IsNullOrWhiteSpace(declared))
        {
            return declared;
        }

        int? uid = EffectiveUserId();

        return uid == null ? null : $"/run/user/{uid.Value}";
    }

    /// <summary>
    /// Where this binary lives. A build output is reported as <see cref="InstallMode.Source"/>
    /// because its udev rules and desktop entry belong to a different binary than the running one.
    /// </summary>
    public static InstallMode DetectInstallMode()
    {
        string baseDirectory;

        try
        {
            baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Could not resolve the base directory: {ex.Message}");
            return InstallMode.Unknown;
        }

        if (LooksLikeBuildOutput(baseDirectory))
        {
            return InstallMode.Source;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home) &&
            baseDirectory.StartsWith(Path.Combine(home, ".local"), StringComparison.Ordinal))
        {
            return IsSteamOs() ? InstallMode.SteamOs : InstallMode.User;
        }

        if (baseDirectory.StartsWith("/usr/", StringComparison.Ordinal) ||
            baseDirectory.StartsWith("/opt/", StringComparison.Ordinal))
        {
            return InstallMode.System;
        }

        return InstallMode.Unknown;
    }

    /// <summary>True when os-release identifies this system as SteamOS.</summary>
    public static bool IsSteamOs()
    {
        IReadOnlyDictionary<string, string> osRelease = ReadOsRelease();

        if (osRelease.TryGetValue("ID", out string id) &&
            id.Equals("steamos", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return osRelease.TryGetValue("ID_LIKE", out string like) &&
               like.Contains("steamos", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The process and OS architecture, for the architecture check.</summary>
    public static (Architecture Os, Architecture Process) Architectures()
        => (RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);

    private static bool LooksLikeBuildOutput(string baseDirectory)
    {
        if (baseDirectory.Contains("/bin/Debug/", StringComparison.Ordinal) ||
            baseDirectory.Contains("/bin/Release/", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            DirectoryInfo directory = new(baseDirectory);

            for (int depth = 0; (directory != null) && (depth < 6); depth++)
            {
                if ((directory.GetFiles("*.csproj").Length > 0) ||
                    (directory.GetFiles("*.slnx").Length > 0) ||
                    (directory.GetFiles("*.sln").Length > 0))
                {
                    return true;
                }

                directory = directory.Parent;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Could not walk up from the base directory: {ex.Message}");
        }

        return false;
    }

    private static string FirstNonEmpty(params string[] variableNames)
    {
        foreach (string name in variableNames)
        {
            string value = Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
