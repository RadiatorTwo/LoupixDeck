using System.Net.Sockets;
using System.Text;
using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Drives HyprlandFocusSource against fake unix sockets: the window lookup goes
/// over the command socket byte-for-byte like hyprctl, and a failed lookup drops
/// the event instead of ending the stream.
/// </summary>
public class HyprlandFocusSourceTests
{
    [Fact]
    public async Task WatchAsync_ResolvesWindowThroughCommandSocket()
    {
        using IpcFixture ipc = IpcFixture.Create(withCommandSocket: true);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        Task<string> commandReply = ipc.ServeCommandAsync(
            """[{"address":"0xABC","class":"kitty","title":"one — two","pid":4242}]""",
            cts.Token);
        Task eventFeed = ipc.ServeEventAsync(cts.Token, "activewindowv2>>abc");

        HyprlandFocusSource source = new(ipc.EventPath, ipc.CommandPath);
        List<FocusInfo?> received = [];

        await foreach (FocusInfo? focus in source.WatchAsync(cts.Token))
        {
            received.Add(focus);
        }

        FocusInfo? only = Assert.Single(received);
        Assert.NotNull(only);
        // Real formats on purpose: the event sends bare hex, j/clients "0x"-prefixed hex,
        // and the case differs too - the match ignores both.
        Assert.Equal("kitty", only!.AppId);
        Assert.Equal("one — two", only.Title);
        Assert.Equal(4242, only.Pid);

        // The exact bytes hyprctl writes: raw "j/clients", no newline, no NUL.
        Assert.Equal("j/clients", await commandReply);
        await eventFeed;
    }

    [Fact]
    public async Task WatchAsync_CommandSocketFailure_DropsEventAndKeepsStream()
    {
        using IpcFixture ipc = IpcFixture.Create(withCommandSocket: false);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // First event needs a lookup the missing command socket refuses (SocketException);
        // the second carries no focus and needs none, so receiving it proves the stream
        // survived the failed lookup.
        Task eventFeed = ipc.ServeEventAsync(
            cts.Token, "activewindowv2>>dead", "activewindowv2>>");

        HyprlandFocusSource source = new(ipc.EventPath, ipc.CommandPath);
        List<FocusInfo?> received = [];

        await foreach (FocusInfo? focus in source.WatchAsync(cts.Token))
        {
            received.Add(focus);
        }

        FocusInfo? only = Assert.Single(received);
        Assert.Null(only);
        await eventFeed;
    }

    /// <summary>
    /// A temp directory holding a listening event socket, plus a command socket that
    /// either listens or (for the failure case) simply does not exist. Every server
    /// call honours the test token so a regression fails instead of hanging.
    /// </summary>
    private sealed class IpcFixture : IDisposable
    {
        private readonly string _directory;
        private readonly Socket _eventListener;
        private readonly Socket? _commandListener;

        private IpcFixture(string directory, Socket eventListener, Socket? commandListener)
        {
            _directory = directory;
            _eventListener = eventListener;
            _commandListener = commandListener;
        }

        public string EventPath { get; private init; } = "";

        public string CommandPath { get; private init; } = "";

        public static IpcFixture Create(bool withCommandSocket)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "hypr-test-" + Guid.NewGuid().ToString("n")[..8]);
            Directory.CreateDirectory(directory);

            string eventPath = Path.Combine(directory, "event.sock");
            string commandPath = Path.Combine(directory, "command.sock");

            IpcFixture fixture = new(directory, Listen(eventPath),
                withCommandSocket ? Listen(commandPath) : null)
            {
                EventPath = eventPath,
                // Without a listener the path simply never exists: Connect throws ENOENT.
                CommandPath = withCommandSocket ? commandPath : Path.Combine(directory, "absent.sock"),
            };
            return fixture;
        }

        private static Socket Listen(string path)
        {
            Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(4);
            return listener;
        }

        /// <summary>Accepts one lookup, records the request bytes, replies and closes.</summary>
        public async Task<string> ServeCommandAsync(string reply, CancellationToken cancellationToken)
        {
            using Socket client = await _commandListener!.AcceptAsync(cancellationToken);

            byte[] buffer = new byte[256];
            int read = await client.ReceiveAsync(buffer, cancellationToken);
            string request = Encoding.UTF8.GetString(buffer, 0, read);

            await client.SendAsync(Encoding.UTF8.GetBytes(reply), cancellationToken);
            client.Shutdown(SocketShutdown.Send); // hyprctl reads until this EOF
            return request;
        }

        /// <summary>Serves the event lines in order, then closes the stream (EOF).</summary>
        public async Task ServeEventAsync(CancellationToken cancellationToken, params string[] lines)
        {
            using Socket client = await _eventListener.AcceptAsync(cancellationToken);
            string payload = string.Join('\n', lines) + "\n";
            await client.SendAsync(Encoding.UTF8.GetBytes(payload), cancellationToken);
            client.Shutdown(SocketShutdown.Send);
        }

        public void Dispose()
        {
            _eventListener.Dispose();
            _commandListener?.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Socket files can linger a beat after dispose; the temp dir evaporates on reboot.
            }
        }
    }
}