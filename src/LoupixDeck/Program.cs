using System.Net;
using System.Net.Sockets;
using Avalonia;

namespace LoupixDeck;

sealed class Program
{
#if !WINDOWS
    private const string SocketPath = "/tmp/loupixdeck_app.sock";
    private static Socket _listenerSocket;
#else
    private const string MutexName = "LoupixDeck_Mutex";
    private static bool _mutexOwned;
    private static Mutex _instanceMutex;
#endif

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
#if !WINDOWS
        {
            if (File.Exists(SocketPath))
            {
                try
                {
                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    client.Connect(new UnixDomainSocketEndPoint(SocketPath));
                    Console.WriteLine("Already running.");
                    return;
                }
                catch (SocketException)
                {
                    File.Delete(SocketPath);
                }
            }

            _listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listenerSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _listenerSocket.Listen(1);

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _listenerSocket.Close();
                File.Delete(SocketPath);
            };
        }
#else
        _instanceMutex = new Mutex(true, MutexName, out _mutexOwned);

        if (!_mutexOwned)
        {
            Console.WriteLine("Already running.");
            return;
        }
#endif

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}