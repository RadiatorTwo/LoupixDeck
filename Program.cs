using System.IO.Pipes;
using System.Net.Sockets;
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

    private static void RedirectConsoleToLogFile()
    {
        try
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#if DEBUG
            var dir = Path.Combine(home, ".config", "LoupixDeck", "debug");
#else
            var dir = Path.Combine(home, ".config", "LoupixDeck");
#endif
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

        var head = raw.Split(' ', 2)[0];
        var lower = head.ToLowerInvariant();
        string command;

        // page<N> / rotarypage<N>
        if (lower.StartsWith("page") && int.TryParse(lower.AsSpan(4), out var tp))
            command = $"System.GotoPage({tp})";
        else if (lower.StartsWith("rotarypage") && int.TryParse(lower.AsSpan(10), out var rp))
            command = $"System.GotoRotaryPage({rp})";
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

        var svc = Program.AppServices?.GetService<ICommandService>();
        if (svc == null) return "ERROR: app not ready yet";

        // Fire on the UI thread so we don't block the listener thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try { await svc.ExecuteCommand(command); }
            catch (Exception ex) { Console.WriteLine($"[CLI] dispatch failed: {ex.Message}"); }
        });
        return $"OK: {command}";
    }
}
