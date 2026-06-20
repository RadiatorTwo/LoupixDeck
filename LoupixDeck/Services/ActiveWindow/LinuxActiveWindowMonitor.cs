using System.Diagnostics;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.ActiveWindow;

/// <summary>
/// Tracks the foreground window on X11 (and XWayland-hosted apps) by spying on the
/// root window's <c>_NET_ACTIVE_WINDOW</c> property via <c>xprop -spy</c>. Mirrors
/// <c>LinuxSystemPowerService</c>: a long-lived subprocess whose stdout is parsed
/// line by line, the whole loop wrapped in try/catch so a missing <c>xprop</c> or a
/// vanished X11 connection degrades silently to a no-op instead of crashing.
///
/// Pure Wayland sessions (no DISPLAY) are not covered — there is no portable
/// foreground-window protocol across GNOME/KDE/wlroots, so we stay a no-op there.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxActiveWindowMonitor : IActiveWindowMonitor, IDisposable
{
    public event EventHandler<ActiveWindowInfo> ActiveWindowChanged;

    private Process _proc;
    private bool _started;
    private string _lastWindowId;
    private (string Process, string Title) _last;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;

        // DISPLAY is set for X11 sessions and for XWayland-hosted apps. Without it
        // we are on a pure Wayland session (or headless) — nothing to spy on.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Console.WriteLine("[AppSwitch] No DISPLAY — active-window monitor disabled (pure Wayland is unsupported).");
            return;
        }

        _ = Task.Run(Monitor);
    }

    private void Monitor()
    {
        try
        {
            _proc = Process.Start(new ProcessStartInfo
            {
                FileName = "xprop",
                Arguments = "-root -spy _NET_ACTIVE_WINDOW",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (_proc == null) return;

            while (!_proc.StandardOutput.EndOfStream)
            {
                var line = _proc.StandardOutput.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                // e.g. "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x1a00007"
                var windowId = ParseWindowId(line);
                if (string.IsNullOrEmpty(windowId)) continue;
                if (windowId == _lastWindowId) continue;
                _lastWindowId = windowId;

                Resolve(windowId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppSwitch] Linux monitor unavailable: {ex.Message}");
        }
    }

    private static string ParseWindowId(string line)
    {
        var hash = line.LastIndexOf('#');
        if (hash < 0) return null;
        var id = line[(hash + 1)..].Trim();
        // "0x0" means no active window (e.g. all windows minimised).
        if (string.IsNullOrEmpty(id) || id == "0x0") return null;
        return id;
    }

    private void Resolve(string windowId)
    {
        try
        {
            // Short-lived query for this window's pid + title. The window may already
            // be gone between the spy event and this call, hence its own try/catch.
            using var query = Process.Start(new ProcessStartInfo
            {
                FileName = "xprop",
                Arguments = $"-id {windowId} _NET_WM_PID _NET_WM_NAME WM_NAME",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (query == null) return;

            var output = query.StandardOutput.ReadToEnd();
            query.WaitForExit();

            var processName = ResolveProcessName(output);
            var title = ResolveTitle(output);

            if (_last.Process == processName && _last.Title == title) return;
            _last = (processName, title);

            ActiveWindowChanged?.Invoke(this, new ActiveWindowInfo
            {
                ProcessName = processName,
                Title = title
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppSwitch] window resolve failed ({windowId}): {ex.Message}");
        }
    }

    private static string ResolveProcessName(string xpropOutput)
    {
        // "_NET_WM_PID(CARDINAL) = 1234"
        foreach (var line in xpropOutput.Split('\n'))
        {
            if (!line.StartsWith("_NET_WM_PID")) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            if (!int.TryParse(line[(eq + 1)..].Trim(), out var pid)) continue;

            try
            {
                // /proc/<pid>/comm holds the bare command name — matches the
                // Windows ProcessName semantics ("chrome", no path, no ".exe").
                var comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
                return string.IsNullOrEmpty(comm) ? null : comm;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static string ResolveTitle(string xpropOutput)
    {
        // Prefer _NET_WM_NAME (UTF8), fall back to WM_NAME. Both look like:
        //   _NET_WM_NAME(UTF8_STRING) = "Title"
        return ExtractQuoted(xpropOutput, "_NET_WM_NAME")
               ?? ExtractQuoted(xpropOutput, "WM_NAME")
               ?? string.Empty;
    }

    private static string ExtractQuoted(string xpropOutput, string prop)
    {
        foreach (var line in xpropOutput.Split('\n'))
        {
            if (!line.StartsWith(prop)) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) return null;
            var value = line[(eq + 1)..].Trim();
            // "no such atom" / unset properties print without a value.
            if (string.IsNullOrEmpty(value)) return null;
            // Strip the surrounding quotes Xlib prints for string properties.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            return value;
        }
        return null;
    }

    public void Dispose()
    {
        try { _proc?.Kill(); } catch { /* ignore */ }
        _proc?.Dispose();
    }
}
