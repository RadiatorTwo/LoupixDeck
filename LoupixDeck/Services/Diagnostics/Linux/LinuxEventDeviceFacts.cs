using System.Text.RegularExpressions;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>One evdev node the recorder would consider, as listed in /proc/bus/input/devices.</summary>
/// <param name="Node">The /dev/input/eventN path.</param>
/// <param name="IsKeyboard">The device exposes the "kbd" handler.</param>
/// <param name="IsPointer">The device exposes the "mouse" handler.</param>
public sealed record EventDeviceCandidate(string Node, bool IsKeyboard, bool IsPointer);

/// <summary>
/// Discovers the evdev nodes macro recording would read, using the same source and the same
/// filters as LinuxInputRecorder.DiscoverKeyboardNodes, so the diagnostics report on exactly
/// the devices the recorder would use.
/// </summary>
internal static partial class LinuxEventDeviceFacts
{
    /// <summary>The prefix LinuxInputRecorder skips, so playback cannot feed back into recording.</summary>
    private const string VirtualDeviceNamePrefix = "Loupix";

    [GeneratedRegex(@"event\d+")]
    private static partial Regex EventNodeRegex();

    /// <summary>
    /// The keyboard and pointer nodes listed in /proc/bus/input/devices, excluding LoupixDeck's
    /// own virtual devices. Returns null when the list itself cannot be read.
    /// </summary>
    public static IReadOnlyList<EventDeviceCandidate> Discover()
    {
        string content;

        try
        {
            content = File.ReadAllText("/proc/bus/input/devices");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Cannot read the input device list: {ex.Message}");
            return null;
        }

        List<EventDeviceCandidate> candidates = [];

        foreach (string block in content.Split("\n\n"))
        {
            string[] lines = block.Split('\n');
            string handlers = lines.FirstOrDefault(line => line.StartsWith("H: Handlers=", StringComparison.Ordinal));

            if (handlers == null)
            {
                continue;
            }

            bool isKeyboard = handlers.Contains("kbd", StringComparison.Ordinal);
            bool isPointer = handlers.Contains("mouse", StringComparison.Ordinal);

            if (!isKeyboard && !isPointer)
            {
                continue;
            }

            string nameLine = lines.FirstOrDefault(line => line.StartsWith("N: Name=", StringComparison.Ordinal));

            if ((nameLine != null) && nameLine.Contains('"'))
            {
                string name = nameLine[(nameLine.IndexOf('"') + 1)..].TrimEnd('"');

                if (name.StartsWith(VirtualDeviceNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            Match match = EventNodeRegex().Match(handlers);

            if (match.Success)
            {
                candidates.Add(new EventDeviceCandidate("/dev/input/" + match.Value, isKeyboard, isPointer));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Opens each candidate read-only and closes it again. A read-only open does not grab the
    /// device - EVIOCGRAB is never issued - and consumes no events, so a running recording and
    /// the user's own typing are unaffected.
    /// </summary>
    /// <returns>The readable keyboard count, the readable pointer count and the first errno seen.</returns>
    public static (int Keyboards, int Pointers, int FirstErrno) ProbeReadable(
        IReadOnlyList<EventDeviceCandidate> candidates, CancellationToken cancellationToken)
    {
        int keyboards = 0;
        int pointers = 0;
        int firstErrno = 0;

        foreach (EventDeviceCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int errno = LinuxInputInterop.TryOpenAndClose(candidate.Node,
                LinuxInputInterop.O_RDONLY | LinuxInputInterop.O_NONBLOCK);

            if (errno != 0)
            {
                if (firstErrno == 0)
                {
                    firstErrno = errno;
                }

                continue;
            }

            if (candidate.IsKeyboard)
            {
                keyboards++;
            }

            if (candidate.IsPointer)
            {
                pointers++;
            }
        }

        return (keyboards, pointers, firstErrno);
    }
}
