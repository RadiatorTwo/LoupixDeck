using System.Reflection;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Progress tick reported to the UI: fraction 0..1, a human-readable status line, and the stable
/// <see cref="SetupSteps"/> key of the logical step currently running (drives the timeline).
/// </summary>
public readonly record struct ProgressReport(double Fraction, string Status, string Step = "");

/// <summary>Outcome of an install/update/repair operation.</summary>
public sealed record OpResult(bool Success, string Message)
{
    public static OpResult Ok(string message) => new(true, message);
    public static OpResult Fail(string message) => new(false, message);
}

/// <summary>Options gathered by the install wizard.</summary>
public sealed class InstallOptions
{
    public string InstallDir { get; set; } = AppPaths.DefaultInstallDir();
    public bool DesktopShortcut { get; set; }
    public bool StartMenuShortcut { get; set; } = true;

    /// <summary>Register LoupixDeck to run at Windows startup (per-user Run key).</summary>
    public bool Autostart { get; set; }

    public bool LaunchAfter { get; set; } = true;
}

/// <summary>
/// Selective repair plan. Each flag re-applies one aspect of the installation. Defaults repair
/// everything except the (destructive) config reset.
/// </summary>
public sealed class RepairPlan
{
    public bool ProgramFiles { get; set; } = true;
    public bool Plugins { get; set; } = true;
    public bool Shortcuts { get; set; } = true;
    public bool Autostart { get; set; } = true;

    /// <summary>Reset the user configuration by backing it up and removing the original.</summary>
    public bool DeleteConfig { get; set; }

    /// <summary>A plan that repairs every non-destructive aspect (used for silent repair).</summary>
    public static RepairPlan Full => new();
}

/// <summary>
/// Orchestrates the setup operations. All heavy work runs off the UI thread; progress is pushed through
/// <see cref="IProgress{T}"/>. Application files live in the install directory; plugins are relocated to
/// the per-user dir; user config under <c>~/.config/LoupixDeck</c> is only touched by an explicit repair
/// "reset config" (which backs it up first) or by uninstall.
/// </summary>
public sealed class InstallService
{
    private readonly PayloadExtractor _extractor = new();

    /// <summary>Product version taken from the assembly (CI injects it via <c>/p:Version</c>).</summary>
    public string Version { get; } = ResolveVersion();

    public bool HasPayload => _extractor.HasPayload;

    private static string ResolveVersion()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    // ──────────────────────────── Install ────────────────────────────

    public Task<OpResult> InstallAsync(InstallOptions options, IProgress<ProgressReport>? progress)
        => Task.Run(() => Install(options, progress));

    private OpResult Install(InstallOptions options, IProgress<ProgressReport>? progress)
    {
        try
        {
            string installDir = options.InstallDir;
            Report(progress, 0.02, "Preparing…", SetupSteps.Prepare);

            // If reinstalling over a running instance, stop it first.
            if (RunningAppControl.IsRunning(installDir))
            {
                Report(progress, 0.04, "Closing running LoupixDeck…", SetupSteps.Prepare);
                RunningAppControl.StopRunningApp(installDir, TimeSpan.FromSeconds(20));
            }

            List<string> pluginIds = ExtractPayload(installDir, ExtractScope.All, progress, 0.05, 0.78);

            Report(progress, 0.82, "Copying uninstaller…", SetupSteps.Uninstaller);
            WriteUninstaller(installDir);

            Report(progress, 0.86, "Creating shortcuts…", SetupSteps.Shortcuts);
            CreateShortcuts(installDir, options.StartMenuShortcut, options.DesktopShortcut);

            Report(progress, 0.90, "Configuring autostart…", SetupSteps.Autostart);
            AutostartService.Apply(Path.Combine(installDir, AppPaths.AppExeName), options.Autostart);

            Report(progress, 0.94, "Registering in Windows…", SetupSteps.Register);
            long size = FileOps.DirectorySize(installDir);
            UninstallRegistry.Register(installDir, Version, size);
            WriteManifest(installDir, pluginIds, options.DesktopShortcut, options.StartMenuShortcut, options.Autostart);

            Report(progress, 1.0, "Done.", SetupSteps.Finalize);

            if (options.LaunchAfter)
                RunningAppControl.StartApp(installDir);

            return OpResult.Ok($"LoupixDeck {Version} was installed to\n{installDir}");
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"Installation failed: {ex.Message}");
        }
    }

    // ──────────────────────────── Update ────────────────────────────

    public Task<OpResult> UpdateAsync(string installDir, bool restartAfter, IProgress<ProgressReport>? progress)
        => Task.Run(() => Update(installDir, restartAfter, progress));

    private OpResult Update(string installDir, bool restartAfter, IProgress<ProgressReport>? progress)
    {
        string backupDir = installDir.TrimEnd(Path.DirectorySeparatorChar) + ".bak";
        bool backedUp = false;

        // Read the previous choices before the backup move relocates the manifest.
        InstallManifest? prev = InstallManifest.TryLoad(installDir);
        bool startMenu = prev?.StartMenuShortcut ?? true;
        bool desktop = prev?.DesktopShortcut ?? false;
        bool autostart = prev?.Autostart ?? false;

        try
        {
            Report(progress, 0.02, "Closing running LoupixDeck…", SetupSteps.StopApp);
            RunningAppControl.StopRunningApp(installDir, TimeSpan.FromSeconds(20));

            // Keep the previous version until the update succeeds.
            FileOps.TryDeleteDirectory(backupDir);
            if (Directory.Exists(installDir))
            {
                Report(progress, 0.08, "Backing up current version…", SetupSteps.Backup);
                Directory.Move(installDir, backupDir);
                backedUp = true;
            }

            List<string> pluginIds = ExtractPayload(installDir, ExtractScope.All, progress, 0.10, 0.80);

            Report(progress, 0.82, "Copying uninstaller…", SetupSteps.Uninstaller);
            WriteUninstaller(installDir);

            Report(progress, 0.86, "Updating shortcuts…", SetupSteps.Shortcuts);
            CreateShortcuts(installDir, startMenu, desktop);

            Report(progress, 0.90, "Updating autostart…", SetupSteps.Autostart);
            AutostartService.Apply(Path.Combine(installDir, AppPaths.AppExeName), autostart);

            Report(progress, 0.94, "Updating registration…", SetupSteps.Register);
            long size = FileOps.DirectorySize(installDir);
            UninstallRegistry.Register(installDir, Version, size);
            WriteManifest(installDir, pluginIds, desktop, startMenu, autostart);

            Report(progress, 0.98, "Cleaning up…", SetupSteps.Cleanup);
            FileOps.TryDeleteDirectory(backupDir);

            Report(progress, 1.0, "Done.", SetupSteps.Finalize);
            if (restartAfter)
                RunningAppControl.StartApp(installDir);

            return OpResult.Ok($"LoupixDeck was updated to {Version}.");
        }
        catch (Exception ex)
        {
            // Roll back to the previous version.
            try
            {
                if (backedUp)
                {
                    FileOps.TryDeleteDirectory(installDir);
                    if (Directory.Exists(backupDir))
                        Directory.Move(backupDir, installDir);
                }
            }
            catch
            {
                // best effort — surface the original error below
            }

            return OpResult.Fail($"Update failed and was rolled back: {ex.Message}");
        }
    }

    // ──────────────────────────── Repair ────────────────────────────

    public Task<OpResult> RepairAsync(string installDir, RepairPlan plan, bool restartAfter,
        IProgress<ProgressReport>? progress)
        => Task.Run(() => Repair(installDir, plan, restartAfter, progress));

    private OpResult Repair(string installDir, RepairPlan plan, bool restartAfter,
        IProgress<ProgressReport>? progress)
    {
        try
        {
            Report(progress, 0.02, "Closing running LoupixDeck…", SetupSteps.StopApp);
            RunningAppControl.StopRunningApp(installDir, TimeSpan.FromSeconds(20));

            // Resetting the config removes the plugins dir with it, so a plugins re-extract is forced
            // afterwards to leave a working set behind.
            bool extractPlugins = plan.Plugins || plan.DeleteConfig;

            if (plan.DeleteConfig)
            {
                Report(progress, 0.08, "Backing up and resetting configuration…", SetupSteps.Config);
                BackupAndResetConfig();
            }

            List<string> pluginIds = InstallManifest.TryLoad(installDir)?.Plugins ?? new List<string>();

            if (plan.ProgramFiles)
            {
                ExtractPayload(installDir, ExtractScope.AppOnly, progress, 0.12, 0.62, SetupSteps.Files);

                Report(progress, 0.64, "Restoring uninstaller…", SetupSteps.Uninstaller);
                WriteUninstaller(installDir);
            }

            if (extractPlugins)
                pluginIds = ExtractPayload(installDir, ExtractScope.PluginsOnly, progress, 0.66, 0.86,
                    SetupSteps.Plugins);

            InstallManifest? prev = InstallManifest.TryLoad(installDir);
            bool startMenu = prev?.StartMenuShortcut ?? true;
            bool desktop = prev?.DesktopShortcut ?? false;
            bool autostart = prev?.Autostart ?? false;

            if (plan.Shortcuts)
            {
                Report(progress, 0.88, "Repairing shortcuts…", SetupSteps.Shortcuts);
                CreateShortcuts(installDir, startMenu, desktop);
            }

            if (plan.Autostart)
            {
                Report(progress, 0.92, "Repairing autostart…", SetupSteps.Autostart);
                AutostartService.Apply(Path.Combine(installDir, AppPaths.AppExeName), autostart);
            }

            Report(progress, 0.96, "Updating registration…", SetupSteps.Register);
            long size = FileOps.DirectorySize(installDir);
            UninstallRegistry.Register(installDir, Version, size);
            WriteManifest(installDir, pluginIds, desktop, startMenu, autostart);

            Report(progress, 1.0, "Done.", SetupSteps.Finalize);
            if (restartAfter)
                RunningAppControl.StartApp(installDir);

            return OpResult.Ok("LoupixDeck was repaired.");
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"Repair failed: {ex.Message}");
        }
    }

    // ──────────────────────────── Shared steps ────────────────────────────

    private List<string> ExtractPayload(string installDir, ExtractScope scope,
        IProgress<ProgressReport>? progress, double from, double to, string step = SetupSteps.Files)
    {
        if (!_extractor.HasPayload)
        {
            Report(progress, to, "No payload embedded (dev build) — skipping extraction.", step);
            return new List<string>();
        }

        string userPlugins = AppPaths.UserPluginsRoot();
        ExtractResult result = _extractor.Extract(installDir, userPlugins, scope, (frac, status) =>
            Report(progress, from + (to - from) * frac, status, step));
        return result.PluginIds;
    }

    /// <summary>
    /// Backs up the user configuration by renaming it to a timestamped sibling directory, then leaving
    /// the original gone so the app regenerates defaults on next launch. Best-effort: absent config is a
    /// no-op.
    /// </summary>
    private static void BackupAndResetConfig()
    {
        string config = AppPaths.ConfigRoot();
        if (!Directory.Exists(config))
            return;

        string backup = config.TrimEnd(Path.DirectorySeparatorChar)
                        + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.Move(config, backup);
    }

    private void WriteManifest(string installDir, List<string> pluginIds, bool desktop,
        bool startMenu, bool autostart)
    {
        new InstallManifest
        {
            Version = Version,
            InstallDir = installDir,
            DesktopShortcut = desktop,
            StartMenuShortcut = startMenu,
            Autostart = autostart,
            Plugins = pluginIds,
            InstalledAtUtc = DateTime.UtcNow.ToString("O")
        }.Save(installDir);
    }

    /// <summary>
    /// Writes the dedicated uninstaller (embedded as <c>uninstaller.exe</c>) into the install dir. This
    /// is a tiny standalone exe (no payload), unlike the full setup — so the install dir stays small.
    /// </summary>
    private static void WriteUninstaller(string installDir)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("uninstaller.exe");
        if (s == null)
            return; // dev build without an embedded uninstaller

        Directory.CreateDirectory(installDir);
        string target = Path.Combine(installDir, AppPaths.UninstallerExeName);
        using FileStream fs = File.Create(target);
        s.CopyTo(fs);
    }

    private static void CreateShortcuts(string installDir, bool startMenu, bool desktop)
    {
        string appExe = Path.Combine(installDir, AppPaths.AppExeName);

        if (startMenu)
        {
            string path = Path.Combine(AppPaths.StartMenuProgramsDir(), AppPaths.ShortcutFileName);
            ShortcutService.Create(path, appExe, appExe, "LoupixDeck");
        }

        if (desktop)
        {
            string path = Path.Combine(AppPaths.DesktopDir(), AppPaths.ShortcutFileName);
            ShortcutService.Create(path, appExe, appExe, "LoupixDeck");
        }
    }

    private static void Report(IProgress<ProgressReport>? progress, double fraction, string status,
        string step = "")
        => progress?.Report(new ProgressReport(fraction, status, step));
}
