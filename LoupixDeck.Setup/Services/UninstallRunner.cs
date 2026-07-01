using System.Diagnostics;

namespace LoupixDeck.Setup.Services;

/// <summary>Result of an uninstall run.</summary>
public readonly record struct UninstallOutcome(bool Success, string Message, bool HandedOff);

/// <summary>
/// Payload-free uninstall orchestration shared by the dedicated <c>LoupixDeck.Uninstall</c> tool (which
/// is what gets registered in the Windows uninstall list) and available to any other caller. Stops a
/// running instance, removes shortcuts, the uninstall registration, the autostart Run key and — only
/// when explicitly requested — the user data, then deletes the install directory. User config and
/// plugins (under <c>~/.config/LoupixDeck</c>) are preserved by default. Because the running exe usually
/// lives inside the install directory it can't delete itself, so the final removal is handed off to a
/// temp copy of the same exe.
/// </summary>
public static class UninstallRunner
{
    public static UninstallOutcome Run(string installDir, bool removeUserData, bool fromTemp,
        Action<double, string>? progress = null)
    {
        try
        {
            Report(progress, 0.05, "Closing running LoupixDeck…");
            RunningAppControl.StopRunningApp(installDir, TimeSpan.FromSeconds(20));

            Report(progress, 0.25, "Removing shortcuts…");
            RemoveShortcuts();

            Report(progress, 0.40, "Removing registration…");
            UninstallRegistry.Unregister();

            Report(progress, 0.48, "Removing autostart entry…");
            AutostartService.Remove();

            if (removeUserData)
            {
                Report(progress, 0.55, "Removing user data…");
                FileOps.TryDeleteDirectory(AppPaths.ConfigRoot());
            }

            // If we're running from inside the install dir, we can't delete ourselves. Hand off to a
            // temp copy that finishes the job once this process exits.
            string? selfPath = Environment.ProcessPath;
            bool runningFromInstall = selfPath != null &&
                Path.GetFullPath(selfPath).StartsWith(
                    Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);

            if (runningFromInstall && !fromTemp)
            {
                Report(progress, 0.70, "Finishing uninstall…");
                HandOffRemoval(installDir, removeUserData);
                return new UninstallOutcome(true, "Uninstall is finishing…", HandedOff: true);
            }

            Report(progress, 0.80, "Removing application files…");
            bool deleted = FileOps.TryDeleteDirectory(installDir);

            if (fromTemp)
                ScheduleSelfDelete();

            Report(progress, 1.0, "Done.");
            return deleted
                ? new UninstallOutcome(true, "LoupixDeck was uninstalled.", HandedOff: false)
                : new UninstallOutcome(false, $"Some files under {installDir} could not be removed.", HandedOff: false);
        }
        catch (Exception ex)
        {
            return new UninstallOutcome(false, $"Uninstall failed: {ex.Message}", HandedOff: false);
        }
    }

    private static void RemoveShortcuts()
    {
        TryDeleteFile(Path.Combine(AppPaths.StartMenuProgramsDir(), AppPaths.ShortcutFileName));
        TryDeleteFile(Path.Combine(AppPaths.DesktopDir(), AppPaths.ShortcutFileName));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Copies this exe to %TEMP% and relaunches it to delete the (now vacated) install dir.</summary>
    private static void HandOffRemoval(string installDir, bool removeUserData)
    {
        string self = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, AppPaths.UninstallerExeName);
        string tempExe = Path.Combine(Path.GetTempPath(), $"LoupixDeck-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(self, tempExe, overwrite: true);

        ProcessStartInfo psi = new()
        {
            FileName = tempExe,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add("--from-temp");
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--dir");
        psi.ArgumentList.Add(installDir);
        if (removeUserData)
            psi.ArgumentList.Add("--remove-user-data");

        Process.Start(psi);
    }

    /// <summary>Fire-and-forget deletion of the temp uninstaller exe after this process exits.</summary>
    private static void ScheduleSelfDelete()
    {
        try
        {
            string self = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(self))
                return;

            // cmd waits a moment for our process handle to release, then deletes the temp exe.
            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch
        {
            // temp file will be cleaned by the OS eventually
        }
    }

    private static void Report(Action<double, string>? progress, double fraction, string status)
        => progress?.Invoke(fraction, status);
}
