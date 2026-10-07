using LoupixDeck.Registry;

namespace LoupixDeck.Utils;

/// <summary>
/// A device that exists only in this process: start with <c>--virtual-device &lt;slug&gt;</c> or set
/// <c>LOUPIXDECK_VIRTUAL_DEVICE=&lt;slug&gt;</c> (e.g. <c>loupedeck-ct</c>) and the app brings that model
/// up without any hardware attached. Everything above the transport runs unchanged — the
/// controller renders and encodes every frame exactly as for a real unit — but the serial link is
/// replaced by <see cref="LoupedeckDevice.Serial.VirtualSerialConnection"/>, which acknowledges each
/// command, drops the bytes, and mirrors the framebuffers into the simulator window.
/// <para>
/// It needs no device at all and is available in Release builds, so a developer without a given
/// model can drive its full UI on any platform.
/// </para>
/// <para>
/// The virtual unit carries the serial <see cref="Serial"/>, so it always has its own config file
/// (<c>config_&lt;slug&gt;_VIRTUAL.json</c>) and never touches a real device's configuration.
/// </para>
/// </summary>
public static class VirtualDevice
{
    public const string Argument = "--virtual-device";
    public const string EnvVar = "LOUPIXDECK_VIRTUAL_DEVICE";

    /// <summary>Serial of the virtual unit; scopes its config file away from real ones.</summary>
    public const string Serial = "VIRTUAL";

    /// <summary>Port prefix that selects the virtual transport instead of a serial port.</summary>
    private const string PortScheme = "virtual:";

    private static string _cliSlug;
    private static int _reported;

    /// <summary>The slug passed on the command line, if any. A restart passes it on again.</summary>
    public static string CliSlug => _cliSlug;

    /// <summary>
    /// Takes <c>--virtual-device &lt;slug&gt;</c> (or <c>--virtual-device=&lt;slug&gt;</c>) out of
    /// <paramref name="args"/> and remembers it. Removed so the single-instance gate does not
    /// forward it to an already running instance as a CLI command.
    /// </summary>
    public static string[] ExtractArgument(string[] args)
    {
        if (args == null || args.Length == 0)
            return args;

        List<string> rest = new(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals(Argument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    _cliSlug = args[++i];
                continue;
            }

            if (arg.StartsWith(Argument + "=", StringComparison.OrdinalIgnoreCase))
            {
                _cliSlug = arg[(Argument.Length + 1)..];
                continue;
            }

            rest.Add(arg);
        }

        return rest.ToArray();
    }

    /// <summary>The model to bring up virtually, or null when none was requested (or the slug is unknown).</summary>
    public static DeviceRegistry.DeviceInfo Requested
    {
        get
        {
            string slug = _cliSlug ?? Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            DeviceRegistry.DeviceInfo match = DeviceRegistry.SupportedDevices
                .FirstOrDefault(d => string.Equals(d.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));

            // Resolution runs at startup and again on every hot-plug reconcile; say it once.
            if (Interlocked.Exchange(ref _reported, 1) == 0)
            {
                Console.WriteLine(match == null
                    ? $"[VirtualDevice] '{slug}' did not match any registered device; ignoring. " +
                      $"Known slugs: {string.Join(", ", DeviceRegistry.SupportedDevices.Select(d => d.Slug).Distinct())}"
                    : $"[VirtualDevice] Bringing up a virtual '{match.Name}' — no hardware is used.");
            }

            return match;
        }
    }

    /// <summary>The virtual unit as a resolved device, or null when none was requested.</summary>
    public static ResolvedDevice CreateResolved()
    {
        DeviceRegistry.DeviceInfo info = Requested;
        return info == null ? null : new ResolvedDevice(info, Serial);
    }

    public static bool IsVirtualSerial(string serial) =>
        string.Equals(serial, Serial, StringComparison.OrdinalIgnoreCase);

    public static bool IsVirtual(ResolvedDevice device) => device != null && IsVirtualSerial(device.Serial);

    public static string PortFor(DeviceRegistry.DeviceInfo info) => PortScheme + info.Slug;

    public static bool IsVirtualPort(string path) =>
        path != null && path.StartsWith(PortScheme, StringComparison.OrdinalIgnoreCase);
}
