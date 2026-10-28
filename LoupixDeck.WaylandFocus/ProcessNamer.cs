using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace LoupixDeck.WaylandFocus;

/// <summary>
/// Resolves window identity to OS processes via /proc, in layers:
/// <list type="number">
/// <item>exact name match against the app_id and its spellings;</item>
/// <item>desktop-file match — the .desktop file whose stem or
/// StartupWMClass equals the app_id donates its Exec basename and
/// StartupWMClass as further candidate names (fixes Electron/Chromium
/// apps like VS Code whose binary name diverges from the app_id);</item>
/// <item>prefix match for desktop-derived candidates (fixes Vivaldi:
/// app_id "vivaldi-stable", binary "vivaldi-bin", StartupWMClass
/// "vivaldi").</item>
/// </list>
/// When several processes match, worker processes of multi-process
/// applications (identified by marker arguments such as --type= or
/// -contentproc, see <see cref="WorkerMarkers"/>) are dropped in favor
/// of the main process.
/// <para>
/// Compositors that expose PIDs (sway, niri, hyprland) skip all of this —
/// they resolve exactly. COSMIC's protocol does not expose PIDs, so this
/// layered approach is the best-effort equivalent.
/// </para>
/// </summary>
public static partial class ProcessNamer
{
    /// <summary>
    /// Cmdline arguments that identify a worker subprocess of a
    /// multi-process application. Not vendor-neutral by nature — each
    /// application family stamps its own marker — so this is a list to
    /// extend as other families are encountered:
    /// "--type=" for Chromium/Electron (renderer, zygote, GPU, utility),
    /// "-contentproc" for Firefox content processes.
    /// </summary>
    internal static readonly string[] WorkerMarkers = ["--type=", "-contentproc"];

    /// <summary>A process matched by app id.</summary>
    public sealed record ProcessMatch(int Pid, string Name);

    internal static readonly string[] DefaultDesktopDirs =
    [
        "/usr/share/applications",
        "/usr/local/share/applications",
        "/var/lib/flatpak/exports/share/applications",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "applications"),
    ];

    // Terminal titles churn constantly, and every churn event triggers a
    // resolution; re-reading all of /usr/share/applications each time would
    // dominate the cost. Desktop files change only on app installs, so a
    // short TTL trades a little staleness for a much cheaper event path.
    private static readonly TimeSpan DesktopCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (IReadOnlyList<string> Names, DateTime Expires)> DesktopCache = new();

    /// <summary>A candidate process for an app_id, with the signals used to rank it.</summary>
    private sealed record RankedMatch(int Pid, string Name, bool IsWorker, int TokenCount);

    /// <summary>Reads the process name for a PID from /proc (comm, then cmdline basename).</summary>
    public static string? NameFromPid(int pid) => NameFromPid("/proc", pid);

    /// <summary>Testable core: same lookup over an injectable proc root.</summary>
    internal static string? NameFromPid(string procRoot, int pid)
    {
        try
        {
            var comm = File.ReadAllText(Path.Combine(procRoot, pid.ToString(), "comm")).Trim();
            if (comm.Length > 0)
                return comm;

            var arguments = ReadCmdlineTokens(Path.Combine(procRoot, pid.ToString()));
            return arguments.Length > 0 ? Path.GetFileName(arguments[0]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // process exited, or /proc is unavailable
        }
    }

    /// <summary>True when a cmdline argument marks a worker subprocess (see <see cref="WorkerMarkers"/>).</summary>
    private static bool IsWorkerMarker(string argument) =>
        WorkerMarkers.Any(marker => argument.StartsWith(marker, StringComparison.Ordinal));

    /// <summary>
    /// Reads /proc/&lt;pid&gt;/cmdline as raw bytes and tokenizes on both NUL
    /// and space. The space split is required because some multi-process
    /// applications (Chromium, Electron) rewrite their argv in place with
    /// spaces (setproctitle-style), which the kernel then serves verbatim
    /// via /proc.
    /// </summary>
    private static string[] ReadCmdlineTokens(string procEntry)
    {
        using var stream = new FileStream(
            Path.Combine(procEntry, "cmdline"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Encoding.UTF8.GetString(memory.ToArray())
            .Split(['\0', ' '], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Finds processes whose identity plausibly matches a Wayland app_id.
    /// Best ranked match first.
    /// </summary>
    public static IReadOnlyList<ProcessMatch> FindByAppId(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return [];

        return MatchAgainstProc("/proc", Candidates(appId), CachedDesktopCandidates(appId));
    }

    /// <summary>Testable core: same algorithm over an injectable /proc root and desktop-file dirs (uncached).</summary>
    internal static IReadOnlyList<ProcessMatch> FindByAppId(
        string? appId, string procRoot, IReadOnlyList<string> desktopDirs)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return [];

        return MatchAgainstProc(procRoot, Candidates(appId), ComputeDesktopCandidates(appId, desktopDirs));
    }

    private static IReadOnlyList<ProcessMatch> MatchAgainstProc(
        string procRoot,
        IEnumerable<string> directCandidates,
        IEnumerable<string> desktopCandidates)
    {
        var desktop = desktopCandidates.ToList();

        var exactNames = directCandidates.Concat(desktop)
            .Select(Normalize)
            .Where(c => c.Length > 0)
            .ToHashSet();
        // Prefix matching only for desktop-derived names: they are the ones
        // that legitimately diverge from the binary ("vivaldi" ~ "vivaldi-bin").
        var prefixNames = desktop
            .Select(Normalize)
            .Where(c => c.Length >= 4)
            .ToList();

        var matches = new List<RankedMatch>();

        foreach (var entry in Directory.GetDirectories(procRoot))
        {
            if (!int.TryParse(Path.GetFileName(entry), out var pid)
                || pid == Environment.ProcessId)
                continue;

            string? comm = null;
            string? executable = null;
            string[] arguments = [];
            var isWorker = false;

            try
            {
                comm = File.ReadAllText(Path.Combine(entry, "comm")).Trim();
                arguments = ReadCmdlineTokens(entry);
                executable = arguments.Length > 0 ? Path.GetFileName(arguments[0]) : null;
                isWorker = arguments.Any(IsWorkerMarker);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // process exited mid-scan
            }

            var commNorm = string.IsNullOrEmpty(comm) ? "" : Normalize(comm);
            var executableNorm = string.IsNullOrEmpty(executable) ? "" : Normalize(executable);

            var exact = exactNames.Contains(commNorm) || exactNames.Contains(executableNorm);
            var prefixed = !exact && prefixNames.Any(c =>
                (commNorm.Length > 0 && commNorm.StartsWith(c, StringComparison.Ordinal))
                || (executableNorm.Length > 0 && executableNorm.StartsWith(c, StringComparison.Ordinal)));

            if (!exact && !prefixed)
                continue;

            var name = !string.IsNullOrEmpty(comm) ? comm : executable;
            if (name is not null)
                matches.Add(new RankedMatch(pid, name, isWorker, arguments.Length));
        }

        // Multi-process applications spawn workers that share the main
        // process's comm (renderers, zygotes, content processes); only the
        // main process lacks a worker marker.
        var mains = matches.Where(m => !m.IsWorker).ToList();
        if (mains.Count == 0)
            mains = matches; // nothing better than workers matched

        // Of the mains, the app itself is invoked bare ("code", "vivaldi-bin")
        // while worker processes carry script paths or flags — so fewest
        // cmdline tokens wins; newest pid breaks remaining ties (the most
        // recently launched instance).
        mains.Sort((a, b) => a.TokenCount != b.TokenCount
            ? a.TokenCount.CompareTo(b.TokenCount)
            : b.Pid.CompareTo(a.Pid));

        return mains.Select(m => new ProcessMatch(m.Pid, m.Name)).ToList();
    }

    /// <summary>
    /// Resolves the owning process and folds it into the event. The
    /// compositor-provided PID wins, but only while its process is still
    /// alive and readable: a PID whose process vanished may already have
    /// been recycled, so it is dropped rather than paired with a name
    /// belonging to a different process (the two halves of an identity
    /// always come from the same process).
    /// Downstream event processors can compare
    /// <see cref="FocusInfo.ProcessName"/> directly.
    /// </summary>
    public static FocusInfo Enrich(FocusInfo focus) => Enrich(focus, "/proc", DefaultDesktopDirs);

    /// <summary>Testable core: same policy over injectable roots.</summary>
    internal static FocusInfo Enrich(
        FocusInfo focus, string procRoot, IReadOnlyList<string> desktopDirs)
    {
        var pid = focus.Pid;
        string? name = pid is { } known ? NameFromPid(procRoot, known) : null;

        if (pid is { } && name is null)
            pid = null; // stale or recycled — discard rather than misattribute

        if (name is null)
        {
            var matches = FindByAppId(focus.AppId, procRoot, desktopDirs);
            if (matches.Count > 0)
            {
                pid = matches[0].Pid;
                name = matches[0].Name;
            }
        }

        return focus with { Pid = pid, ProcessName = name ?? focus.AppId };
    }

    private static IReadOnlyList<string> CachedDesktopCandidates(string appId)
    {
        var now = DateTime.UtcNow;
        if (DesktopCache.TryGetValue(appId, out var hit) && hit.Expires > now)
            return hit.Names;

        var names = ComputeDesktopCandidates(appId, DefaultDesktopDirs).ToList();
        DesktopCache[appId] = (names, now + DesktopCacheTtl);
        return names; // expiry lets newly installed apps become resolvable
    }

    /// <summary>
    /// Candidate binary names donated by the desktop file whose stem or
    /// StartupWMClass equals the app_id: the Exec basename and the
    /// StartupWMClass itself.
    /// </summary>
    private static IEnumerable<string> ComputeDesktopCandidates(
        string appId, IReadOnlyList<string> searchDirs)
    {
        foreach (var directory in searchDirs)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.desktop");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // dir may not exist (e.g. no flatpak installs)
            }

            foreach (var file in files)
            {
                string? executable = null;
                string? wmClass = null;

                foreach (var line in File.ReadLines(file))
                {
                    if (executable is null && line.StartsWith("Exec=", StringComparison.Ordinal))
                        executable = line["Exec=".Length..]
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault();
                    else if (wmClass is null
                        && line.StartsWith("StartupWMClass=", StringComparison.Ordinal))
                        wmClass = line["StartupWMClass=".Length..].Trim();

                    if (executable is not null && wmClass is not null)
                        break;
                }

                var stem = Path.GetFileNameWithoutExtension(file);
                var matches = stem.Equals(appId, StringComparison.OrdinalIgnoreCase)
                    || (wmClass?.Equals(appId, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!matches)
                    continue;

                foreach (var candidate in ExecCandidates(executable))
                    yield return candidate;
                if (!string.IsNullOrEmpty(wmClass))
                    yield return wmClass;
            }
        }
    }

    /// <summary>
    /// Derives candidate binary names from a desktop file's Exec line.
    /// Launcher invocations are unwrapped: "flatpak run org.foo.Bar"
    /// donates the flatpak app id, never the "flatpak" launcher itself —
    /// otherwise every flatpak broker process on the system would match
    /// every flatpak app's window.
    /// </summary>
    private static IEnumerable<string> ExecCandidates(string? exec)
    {
        if (string.IsNullOrEmpty(exec))
            yield break;

        var tokens = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var basename = Path.GetFileName(tokens[0]);

        if (basename.Equals("flatpak", StringComparison.OrdinalIgnoreCase))
        {
            var runIndex = Array.IndexOf(tokens, "run");
            if (runIndex >= 0 && runIndex + 1 < tokens.Length && !tokens[runIndex + 1].StartsWith('-'))
                yield return tokens[runIndex + 1];
            yield break; // never donate "flatpak" itself
        }

        yield return basename;
    }

    /// <summary>Lower-cases and strips every non-alphanumeric character.</summary>
    internal static string Normalize(string value)
        => NonAlphanumeric().Replace(value, string.Empty).ToLowerInvariant();

    /// <summary>App id spellings that could correspond to a binary name.</summary>
    internal static IEnumerable<string> Candidates(string appId)
    {
        yield return appId;

        var lastSegment = appId[(appId.LastIndexOf('.') + 1)..];
        yield return lastSegment;

        var kebab = UpperToLowerBoundary().Replace(lastSegment, "$1-$2");
        if (!string.Equals(kebab, lastSegment, StringComparison.OrdinalIgnoreCase))
            yield return kebab;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex UpperToLowerBoundary();
}