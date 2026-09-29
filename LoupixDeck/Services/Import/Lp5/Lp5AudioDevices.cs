namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Finds a Windows audio output by the friendly name Loupedeck stores, e.g.
/// <c>Headset Earphone (2- CORSAIR VIRTUOSO XT Wireless Gaming Headset)</c>, and returns its endpoint
/// id, which is what the LoupixDeck Audio plugin addresses devices by.
/// </summary>
internal static class Lp5AudioDevices
{
#if WINDOWS
    private const string RenderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    private const string RenderIdPrefix = "{0.0.0.00000000}.";
    private const string DeviceDescription = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string InterfaceName = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";
    private const int ActiveState = 1;
#endif

    /// <summary>
    /// The endpoint id of the output named <paramref name="friendlyName"/>, or null when this computer has
    /// no such output or several that cannot be told apart. Always null off Windows.
    /// </summary>
    public static string RenderEndpointId(string friendlyName)
    {
#if WINDOWS
        // Always true here; tells the platform analyzer (CA1416) that the registry calls below
        // only run on Windows, which it cannot infer from the #if on a net10.0 target.
        if (!OperatingSystem.IsWindows()) return null;

        List<(string Id, bool Active)> matches = [];
        try
        {
            // Fully qualified: the project's own LoupixDeck.Registry namespace shadows an
            // unqualified "Registry" inside LoupixDeck.*.
            using Microsoft.Win32.RegistryKey render = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RenderKey);
            if (render == null) return null;

            foreach (string guid in render.GetSubKeyNames())
            {
                using Microsoft.Win32.RegistryKey endpoint = render.OpenSubKey(guid);
                using Microsoft.Win32.RegistryKey properties = endpoint?.OpenSubKey("Properties");
                if (properties == null) continue;

                // Windows shows an output as "<description> (<interface>)".
                string name = $"{properties.GetValue(DeviceDescription)} ({properties.GetValue(InterfaceName)})";
                if (string.Equals(name, friendlyName, StringComparison.Ordinal))
                    matches.Add((RenderIdPrefix + guid, endpoint.GetValue("DeviceState") is int state && state == ActiveState));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.WriteLine($"[LoupedeckImport] Cannot read the audio devices: {ex.Message}");
            return null;
        }

        // An unplugged twin of the same device keeps its entry; the connected one is meant.
        List<(string Id, bool Active)> active = matches.Where(m => m.Active).ToList();
        if (active.Count == 1) return active[0].Id;
        return active.Count == 0 && matches.Count == 1 ? matches[0].Id : null;
#else
        return null;
#endif
    }
}
