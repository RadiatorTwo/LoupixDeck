using System.IO.Pipes;
using System.Text;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using LoupixDeck.Services;
using LoupixDeck.Utils;

namespace LoupixDeck;

sealed class Program
{
#if !WINDOWS
    private const string SocketPath = "/tmp/loupixdeck_app.sock";
    private static Socket _listenerSocket;
#else
    private const string MutexName = "LoupixDeck_Mutex";
    private const string PipeName = "LoupixDeck_Pipe";
    private static bool _mutexOwned;
    private static Mutex _instanceMutex;
#endif

    /// <summary>Set by App.axaml.cs after the DI container is built so the
    /// CLI command listener can resolve ICommandService at runtime.</summary>
    public static IServiceProvider AppServices { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        InstallCrashLogger(args);
        RedirectConsoleToLogFile();
        Console.WriteLine($"=== LoupixDeck Main {DateTime.Now:yyyy-MM-dd HH:mm:ss} args=[{string.Join(' ', args)}] ===");

#if !WINDOWS
        if (File.Exists(SocketPath))
        {
            // Another instance is (probably) running. If the user passed CLI
            // args, forward them as a command and exit; otherwise just bail.
            if (args.Length > 0)
            {
                ForwardCliToUds(args);
                return;
            }
            try
            {
                using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
                Console.WriteLine("Already running.");
                return;
            }
            catch (SocketException)
            {
                // Stale socket file (previous instance crashed) — clean up and continue.
                File.Delete(SocketPath);
            }
        }

        _listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenerSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listenerSocket.Listen(4);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _listenerSocket.Close();
            try { File.Delete(SocketPath); } catch { /* ignore */ }
        };
        _ = Task.Run(AcceptUdsLoop);
#else
        _instanceMutex = new Mutex(true, MutexName, out _mutexOwned);
        if (!_mutexOwned)
        {
            if (args.Length > 0)
            {
                ForwardCliToPipe(args);
                return;
            }
            Console.WriteLine("Already running.");
            return;
        }
        _ = Task.Run(AcceptPipeLoop);
#endif

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // ──────── CLI channel: client side ────────

#if !WINDOWS
    private static void ForwardCliToUds(string[] args)
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(SocketPath));
            client.Send(Encoding.UTF8.GetBytes(string.Join(' ', args)));
            var buf = new byte[4096];
            var n = client.Receive(buf);
            Console.WriteLine(Encoding.UTF8.GetString(buf, 0, n));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CLI error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void AcceptUdsLoop()
    {
        try
        {
            while (true)
            {
                var client = _listenerSocket.Accept();
                _ = Task.Run(() => HandleUdsClient(client));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI] UDS accept loop ended: {ex.Message}");
        }
    }

    private static void HandleUdsClient(Socket client)
    {
        try
        {
            var buf = new byte[4096];
            var n = client.Receive(buf);
            var raw = Encoding.UTF8.GetString(buf, 0, n).Trim();
            var response = CommandChannel.Dispatch(raw);
            client.Send(Encoding.UTF8.GetBytes(response));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI] UDS handle failed: {ex.Message}");
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }
#else
    private static void ForwardCliToPipe(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(string.Join(' ', args));
            client.Write(bytes, 0, bytes.Length);
            // WaitForPipeDrain is Windows-only; this whole branch only compiles under
            // the WINDOWS constant, but the analyzer needs an explicit OS guard.
            if (OperatingSystem.IsWindows())
                client.WaitForPipeDrain();
            var buf = new byte[4096];
            var n = client.Read(buf, 0, buf.Length);
            Console.WriteLine(Encoding.UTF8.GetString(buf, 0, n));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CLI error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void AcceptPipeLoop()
    {
        while (true)
        {
            try
            {
                var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                server.WaitForConnection();
                _ = Task.Run(() => HandlePipeClient(server));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLI] Pipe accept loop error: {ex.Message}");
                Thread.Sleep(250);
            }
        }
    }

    private static void HandlePipeClient(NamedPipeServerStream pipe)
    {
        try
        {
            var buf = new byte[4096];
            var n = pipe.Read(buf, 0, buf.Length);
            var raw = Encoding.UTF8.GetString(buf, 0, n).Trim();
            var response = CommandChannel.Dispatch(raw);
            var rb = Encoding.UTF8.GetBytes(response);
            pipe.Write(rb, 0, rb.Length);
            // WaitForPipeDrain is Windows-only; this whole branch only compiles under
            // the WINDOWS constant, but the analyzer needs an explicit OS guard.
            if (OperatingSystem.IsWindows())
                pipe.WaitForPipeDrain();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI] Pipe handle failed: {ex.Message}");
        }
        finally
        {
            try { pipe.Dispose(); } catch { }
        }
    }
#endif

#if WINDOWS
    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllocConsole();
#endif

    /// <summary>
    /// Debug builds send console output to the terminal for live diagnostics;
    /// release builds redirect it to a log file. Because this is a WinExe (GUI
    /// subsystem) there is no console on Windows by default, so the debug path
    /// attaches to the launching terminal — or opens a fresh console as a
    /// fallback (e.g. when started from Explorer).
    /// </summary>
    private static void RedirectConsoleToLogFile()
    {
#if DEBUG
#if WINDOWS
        try
        {
            // AttachConsole/AllocConsole both fail (returning false) when a
            // console is already shared from `dotnet run`; that's fine — we just
            // rebind to whatever console we end up with so AutoFlush is on.
            if (!AttachConsole(AttachParentProcess))
                AllocConsole();

            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stdout);
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console may reject */ }
        }
        catch
        {
            // best-effort — leave the default stdout in place
        }
#endif
        // Non-Windows debug builds already have stdout wired to the terminal.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.WriteLine($"UnhandledException: {e.ExceptionObject}");
#else
        try
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(home, ".config", "LoupixDeck");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "loupixdeck-startup.log");
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.WriteLine($"UnhandledException: {e.ExceptionObject}");
                writer.Flush();
            };
        }
        catch
        {
            // best-effort
        }
#endif
    }

    // ──────── Crash diagnostics ────────

    [ThreadStatic] private static bool _inCrashLog;
    private static readonly Lock _crashLogGate = new();

    /// <summary>
    /// Absolute path of the crash log. Written into the user config dir
    /// (~/.config/LoupixDeck), the same writable location as the startup log —
    /// next to the executable would fail for installed release builds (e.g. a
    /// read-only Program Files).
    /// </summary>
    private static string CrashLogPath()
    {
        try
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(home, ".config", "LoupixDeck");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "crash.log");
        }
        catch { return "crash.log"; }
    }

    /// <summary>
    /// Installs process-wide handlers that record unhandled MANAGED exceptions (on any
    /// thread) to <c>crash.log</c> with a full stack trace. This catches background-thread
    /// failures (e.g. "Collection was modified", NullReferenceException in a render/timer
    /// path) that otherwise terminate the process with no visible reason.
    ///
    /// OPT-IN: disabled by default so normal release users get no crash.log and no
    /// handler overhead. Enable with the <c>--crashlog</c> command-line switch when
    /// diagnosing a crash.
    ///
    /// NOTE: a pure NATIVE access violation (e.g. inside Skia) fast-fails the runtime and
    /// is NOT delivered here — for that, run with the .NET minidump env vars
    /// (DOTNET_DbgEnableMiniDump=1, DOTNET_DbgMiniDumpType=2, DOTNET_DbgMiniDumpName=…).
    /// The two are complementary: this for managed crashes, the dump for native ones.
    ///
    /// Pass <c>--firstchance</c> to also log EVERY thrown exception (very noisy — useful to
    /// see the last exception before a crash). It implies <c>--crashlog</c>.
    /// </summary>
    private static void InstallCrashLogger(string[] args)
    {
        var firstChance = args.Any(a => a.Equals("--firstchance", StringComparison.OrdinalIgnoreCase));
        var enabled = firstChance
                      || args.Any(a => a.Equals("--crashlog", StringComparison.OrdinalIgnoreCase));
        if (!enabled)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("AppDomain.UnhandledException", e.ExceptionObject, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception, false);
            e.SetObserved();
        };

        if (firstChance)
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
                WriteCrash("FirstChanceException", e.Exception, false);
        }

        Console.WriteLine($"[CrashLogger] installed → {CrashLogPath()}");
    }

    private static void WriteCrash(string source, object error, bool terminating)
    {
        // Guard against re-entrancy: file IO below can itself raise an exception, which
        // would re-trigger the FirstChance handler and recurse.
        if (_inCrashLog) return;
        _inCrashLog = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ CRASH ================");
            sb.AppendLine($"Time:        {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source:      {source}");
            sb.AppendLine($"Terminating: {terminating}");
            sb.AppendLine($"Thread:      {Environment.CurrentManagedThreadId} '{Thread.CurrentThread.Name}'");
            sb.AppendLine($"Detail:      {error}");
            sb.AppendLine("======================================");

            try
            {
                lock (_crashLogGate)
                    File.AppendAllText(CrashLogPath(), sb.ToString());
            }
            catch { /* disk best-effort */ }

            try { Console.WriteLine(sb.ToString()); } catch { /* console best-effort */ }
        }
        finally
        {
            _inCrashLog = false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>
/// Maps a raw CLI string to a System.* command and dispatches it via
/// ICommandService on the UI thread. Returns a short status reply to the
/// client (printed by the CLI invocation).
/// </summary>
internal static class CommandChannel
{
    public static string Dispatch(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "ERROR: empty command";
        Console.WriteLine($"[CLI] received: {raw}");

        // Optional device selector (issue #116 phase 3b): "--device <serialOrSlug> <command…>"
        // routes the command to that device's own ICommandService; without it the
        // primary device is targeted. Quit/window toggle stay global regardless.
        var target = Program.AppServices;
        var selToken = raw.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (selToken.Length >= 2 &&
            (selToken[0].Equals("--device", StringComparison.OrdinalIgnoreCase) ||
             selToken[0].Equals("-d", StringComparison.OrdinalIgnoreCase)))
        {
            var selector = selToken[1];
            var host = Program.AppServices?.GetService<IDeviceHostRegistry>()?.Find(selector);
            if (host == null) return $"ERROR: no device matching '{selector}'";
            target = host.Provider;
            raw = selToken.Length >= 3 ? selToken[2].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return "ERROR: no command after --device";
            Console.WriteLine($"[CLI] routed to device {host.Device.ScopeKey}");
        }

        var head = raw.Split(' ', 2)[0];
        var lower = head.ToLowerInvariant();
        string command;

        // page<N> / rotarypage<N>
        if (lower.StartsWith("page") && int.TryParse(lower.AsSpan(4), out var tp))
        {
            command = $"System.GotoPage({tp})";
        }
        else if (lower.StartsWith("rotarypage") && int.TryParse(lower.AsSpan(10), out var rp))
        {
            command = $"System.GotoRotaryPage({rp})";
        }
        // Fork-CLI compat: `updatebutton 6 text=Hi backColor=Red` →
        // `System.UpdateButton(6,text=Hi,backColor=Red)`.
        else if (lower == "updatebutton" && raw.Length > head.Length)
        {
            var rest = raw.Substring(head.Length).Trim();
            command = $"System.UpdateButton({rest.Replace(' ', ',')})";
        }
        // Fork-CLI compat: `removelayer 6 MyImage` → `System.RemoveLayer(6,MyImage)`.
        else if (lower == "removelayer" && raw.Length > head.Length)
        {
            var rest = raw.Substring(head.Length).Trim();
            command = $"System.RemoveLayer({rest.Replace(' ', ',')})";
        }
        else
        {
            command = lower switch
            {
                "off"                => "System.DeviceOff",
                "on"                 => "System.DeviceOn",
                "toggle-device" or "on-off" => "System.DeviceToggle",
                "wakeup"             => "System.DeviceWakeup",
                "nextpage"           => "System.NextPage",
                "previouspage"       => "System.PreviousPage",
                "nextrotarypage"     => "System.NextRotaryPage",
                "previousrotarypage" => "System.PreviousRotaryPage",
                "show" or "hide" or "toggle" => "System.ToggleWindow",
                "quit"               => "__quit__",
                _ => raw // assume the user already passed a full System.* command
            };
        }

        if (command == "__quit__")
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (WindowHelper.GetMainWindow() is Views.MainWindow mw) mw.QuitApplication();
                else Environment.Exit(0);
            });
            return "OK: quitting";
        }

        var svc = target?.GetService<ICommandService>();
        if (svc == null) return "ERROR: app not ready yet";

        // Fire on the UI thread so we don't block the listener thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try { await svc.ExecuteCommand(command, LoupixDeck.PluginSdk.ButtonTargets.None); }
            catch (Exception ex) { Console.WriteLine($"[CLI] dispatch failed: {ex.Message}"); }
        });
        return $"OK: {command}";
    }
}
