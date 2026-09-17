using System.Text.RegularExpressions;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Removes the identifying parts of a diagnostic report. It runs over every value the report
/// writes and once more over the finished text, so a check that starts emitting something new
/// cannot leak it by forgetting to ask.
///
/// The order matters: a home directory contains the user name, so it has to be replaced first.
/// </summary>
internal static partial class DiagnosticReportSanitizer
{
    [GeneratedRegex(@"(?<=/run/user/)\d+")]
    private static partial Regex RuntimeUidRegex();

    [GeneratedRegex(@"/home/[^/\s""']+")]
    private static partial Regex HomePathRegex();

    [GeneratedRegex(@"\b\d{1,3}(\.\d{1,3}){3}\b")]
    private static partial Regex IpV4Regex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{1,4}(:[0-9a-fA-F]{1,4}){4,7}\b")]
    private static partial Regex IpV6Regex();

    [GeneratedRegex(@"\b[0-9a-f]{32}\b")]
    private static partial Regex MachineIdRegex();

    /// <summary>
    /// A /dev/serial/by-id link spells the device's full serial out - "…Loupedeck_Live_S_LS1234…".
    /// The device checks report the port they were opened through, which can be exactly such a
    /// link, so the link is reduced to the directory it lives in.
    /// </summary>
    [GeneratedRegex(@"/dev/serial/by-id/[^\s""']+")]
    private static partial Regex SerialByIdRegex();

    /// <summary>Replaces user names, home paths, addresses and machine ids with placeholders.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string scrubbed = text;

        foreach (string home in HomeDirectories())
        {
            scrubbed = scrubbed.Replace(home, "~", StringComparison.Ordinal);
        }

        scrubbed = RuntimeUidRegex().Replace(scrubbed, "<uid>");
        scrubbed = HomePathRegex().Replace(scrubbed, "/home/<user>");

        string userName = Environment.UserName;

        // A very short login would shred unrelated words, and it is not identifying on its own.
        if (!string.IsNullOrEmpty(userName) && (userName.Length >= 3))
        {
            scrubbed = scrubbed.Replace(userName, "<user>", StringComparison.Ordinal);
        }

        scrubbed = IpV4Regex().Replace(scrubbed, "<ip>");
        scrubbed = IpV6Regex().Replace(scrubbed, "<ip>");
        scrubbed = MachineIdRegex().Replace(scrubbed, "<id>");
        scrubbed = SerialByIdRegex().Replace(scrubbed, "/dev/serial/by-id/<device>");

        return scrubbed;
    }

    /// <summary>
    /// Shortens a serial number to its first and last characters. Phase 1 reports no serials;
    /// the device checks of phase 2 do.
    /// </summary>
    public static string ShortenSerial(string serial)
    {
        if (string.IsNullOrEmpty(serial) || (serial.Length <= 6))
        {
            return "<serial>";
        }

        return $"{serial[..2]}…{serial[^2..]}";
    }

    /// <summary>The home directory in every spelling it can appear in, longest first.</summary>
    private static IEnumerable<string> HomeDirectories()
    {
        HashSet<string> homes = new(StringComparer.Ordinal);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            homes.Add(home.TrimEnd('/'));

            try
            {
                homes.Add(Path.GetFullPath(home).TrimEnd('/'));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Diagnostics] Could not resolve the home directory: {ex.Message}");
            }
        }

        return homes.Where(path => path.Length > 1).OrderByDescending(path => path.Length);
    }
}
