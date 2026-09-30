using System.Diagnostics;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Converts a macOS <c>.icns</c> icon into a PNG the picker can decode.
/// </summary>
/// <remarks>
/// Avalonia cannot decode <c>.icns</c>, which is a container of several sizes rather than a
/// plain image. <c>sips</c> is part of the base system and already knows the format, so it is
/// used instead of taking on an icns decoder for this one job.
/// </remarks>
public static class MacAppIcons
{
    /// <summary>Timeout for one conversion, so a wedged process cannot stall an icon list.</summary>
    private const int ConversionTimeoutMs = 10_000;

    public static bool TryConvertIcns(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo("sips")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("format");
            startInfo.ArgumentList.Add("png");
            startInfo.ArgumentList.Add(source);
            startInfo.ArgumentList.Add("--out");
            startInfo.ArgumentList.Add(destination);

            using Process process = Process.Start(startInfo);
            if (process == null)
                return false;

            if (!process.WaitForExit(ConversionTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return false;
            }

            return process.ExitCode == 0 && File.Exists(destination) && new FileInfo(destination).Length > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppIcon] sips failed for '{source}': {ex.Message}");
            return false;
        }
    }
}
