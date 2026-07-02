using System.Diagnostics;
using System.Security.Principal;

namespace LoupixDeck.Setup.Services;

/// <summary>Helpers for detecting write access and relaunching the setup elevated when required.</summary>
public static class ElevationHelper
{
    public static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if a new file can be created under <paramref name="dir"/> (creating parent dirs as
    /// needed). Used to decide whether the chosen install location needs elevation.
    /// </summary>
    public static bool CanWriteTo(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".loupix_write_probe_" + Guid.NewGuid().ToString("N"));
            using (FileStream fs = File.Create(probe))
            {
                fs.WriteByte(0);
            }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches this executable elevated (UAC), preserving the mode and target dir. Returns the started
    /// process, or null if the user declined the prompt.
    /// </summary>
    public static Process? RelaunchElevated(SetupArgs args)
    {
        List<string> argv = new() { "--elevated" };
        switch (args.Mode)
        {
            case SetupMode.Update: argv.Add("--update"); break;
            case SetupMode.Repair: argv.Add("--repair"); break;
        }
        if (args.Silent)
            argv.Add("--silent");
        if (!string.IsNullOrEmpty(args.TargetDir))
        {
            argv.Add("--dir");
            argv.Add(args.TargetDir);
        }

        string? self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self))
            return null; // can't determine our own path to relaunch

        ProcessStartInfo psi = new()
        {
            FileName = self,
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (string a in argv)
            psi.ArgumentList.Add(a);

        try
        {
            return Process.Start(psi);
        }
        catch
        {
            // User declined the UAC prompt.
            return null;
        }
    }
}
