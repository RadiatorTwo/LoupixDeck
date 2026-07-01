using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Setup.Services;

namespace LoupixDeck.Setup.ViewModels;

/// <summary>The top-level view the single window is showing.</summary>
public enum SetupScreen
{
    /// <summary>Fresh-install config, or the "existing installation found" landing.</summary>
    Home,

    /// <summary>The selective-repair options.</summary>
    RepairOptions,

    /// <summary>Operation in progress: step timeline + progress bar.</summary>
    Running,

    /// <summary>Result: completed-step checklist + log/close.</summary>
    Done
}

/// <summary>
/// Drives the one-screen setup. <see cref="Mode"/> (decided once at construction) selects the Home
/// content — fresh install vs the update/repair landing for an existing install. The actually-running
/// operation is tracked separately in <see cref="_op"/> so choosing "Repair" from an update landing
/// doesn't rewrite the Home. Progress reports advance a discrete <see cref="Steps"/> timeline while the
/// free-form status line reports the current file. (Uninstall is a separate tool, launched from here.)
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private readonly InstallService _service = new();
    private readonly StringBuilder _log = new();
    private string _lastLogLine = "";

    /// <summary>The operation currently running (drives the timeline/title while on Running/Done).</summary>
    private SetupMode _op;

    public WizardViewModel(SetupArgs args)
    {
        Mode = args.Mode;

        string? existingDir = UninstallRegistry.GetInstalledLocation();
        DetectedVersion = UninstallRegistry.GetInstalledVersion();
        NewVersion = _service.Version;

        // A default "install" launch over an existing install becomes an update — or a repair when the
        // installed version already matches the payload (nothing to upgrade, only re-extract files).
        if (Mode == SetupMode.Install && !string.IsNullOrEmpty(existingDir) && DetectedVersion != null)
            Mode = IsSameVersion(DetectedVersion, NewVersion) ? SetupMode.Repair : SetupMode.Update;

        _op = Mode;

        InstallDir = args.TargetDir
                     ?? (string.IsNullOrEmpty(existingDir) ? AppPaths.DefaultInstallDir() : existingDir);

        // Preselect repair-autostart to match the existing install's choice.
        RepairAutostart = InstallManifest.TryLoad(InstallDir)?.Autostart ?? false;

        Screen = SetupScreen.Home;
    }

    // ── State ──

    public SetupMode Mode { get; }
    public string? DetectedVersion { get; }
    public string NewVersion { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHome), nameof(ShowInstallConfig), nameof(ShowExistingHome),
        nameof(ShowUpdateCard), nameof(ShowUpToDate), nameof(ShowRepairOptions), nameof(ShowRunning),
        nameof(ShowDone), nameof(HeaderTitle), nameof(HeaderSubtitle))]
    public partial SetupScreen Screen { get; set; }

    [ObservableProperty]
    public partial string InstallDir { get; set; } = "";

    // Install options
    [ObservableProperty] public partial bool DesktopShortcut { get; set; }
    [ObservableProperty] public partial bool StartMenuShortcut { get; set; } = true;
    [ObservableProperty] public partial bool Autostart { get; set; }
    [ObservableProperty] public partial bool LaunchAfter { get; set; } = true;

    // Repair options
    [ObservableProperty] public partial bool RepairProgramFiles { get; set; } = true;
    [ObservableProperty] public partial bool RepairPlugins { get; set; } = true;
    [ObservableProperty] public partial bool RepairShortcuts { get; set; } = true;
    [ObservableProperty] public partial bool RepairAutostart { get; set; }
    [ObservableProperty] public partial bool RepairDeleteConfig { get; set; }

    // Progress / timeline
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    public partial double ProgressValue { get; set; }

    [ObservableProperty] public partial string ProgressStatus { get; set; } = "";

    public ObservableCollection<SetupStepItem> Steps { get; } = new();

    public string ProgressPercentText => $"{(int)Math.Round(ProgressValue)} %";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    public partial bool IsBusy { get; set; }

    // Result
    [ObservableProperty] public partial bool ResultSuccess { get; set; }
    [ObservableProperty] public partial string ResultMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogButtonText))]
    public partial bool ShowLog { get; set; }

    [ObservableProperty] public partial string LogText { get; set; } = "";

    // ── Derived UI state ──

    public bool ShowHome => Screen == SetupScreen.Home;
    public bool ShowInstallConfig => ShowHome && Mode == SetupMode.Install;
    public bool ShowExistingHome => ShowHome && Mode != SetupMode.Install;
    public bool ShowUpdateCard => ShowExistingHome && Mode == SetupMode.Update;
    public bool ShowUpToDate => ShowExistingHome && Mode == SetupMode.Repair;
    public bool ShowRepairOptions => Screen == SetupScreen.RepairOptions;
    public bool ShowRunning => Screen == SetupScreen.Running;
    public bool ShowDone => Screen == SetupScreen.Done;

    public bool CanInteract => !IsBusy;

    public string LogButtonText => ShowLog ? "Hide log" : "Show log";

    public string HeaderTitle => Screen switch
    {
        SetupScreen.RepairOptions => "Repair LoupixDeck",
        SetupScreen.Running => _op switch
        {
            SetupMode.Update => "Updating LoupixDeck",
            SetupMode.Repair => "Repairing LoupixDeck",
            _ => "Installing LoupixDeck"
        },
        SetupScreen.Done => ResultTitle,
        _ => Mode switch
        {
            SetupMode.Update => "Update available",
            SetupMode.Repair => "LoupixDeck is installed",
            _ => "Install LoupixDeck"
        }
    };

    public string HeaderSubtitle => Screen switch
    {
        SetupScreen.RepairOptions => "Choose what to repair.",
        SetupScreen.Running => "Please wait…",
        SetupScreen.Done => "",
        _ => Mode switch
        {
            SetupMode.Update => $"Version {DetectedVersion} → {NewVersion}",
            SetupMode.Repair => $"Version {NewVersion} is installed",
            _ => $"Version {NewVersion}"
        }
    };

    private string ResultTitle => !ResultSuccess
        ? "Something went wrong"
        : _op switch
        {
            SetupMode.Update => "Update installed",
            SetupMode.Repair => "Repair complete",
            _ => "Installation complete"
        };

    // ── Commands ──

    [RelayCommand]
    private Task StartInstall()
    {
        _op = SetupMode.Install;
        return RunAsync(_service.InstallAsync(new InstallOptions
        {
            InstallDir = InstallDir,
            DesktopShortcut = DesktopShortcut,
            StartMenuShortcut = StartMenuShortcut,
            Autostart = Autostart,
            LaunchAfter = LaunchAfter
        }, MakeProgress()));
    }

    [RelayCommand]
    private Task StartUpdate()
    {
        _op = SetupMode.Update;
        return RunAsync(_service.UpdateAsync(InstallDir, restartAfter: LaunchAfter, MakeProgress()));
    }

    [RelayCommand]
    private void OpenRepair() => Screen = SetupScreen.RepairOptions;

    [RelayCommand]
    private Task StartRepair()
    {
        _op = SetupMode.Repair;
        RepairPlan plan = new()
        {
            ProgramFiles = RepairProgramFiles,
            Plugins = RepairPlugins,
            Shortcuts = RepairShortcuts,
            Autostart = RepairAutostart,
            DeleteConfig = RepairDeleteConfig
        };
        return RunAsync(_service.RepairAsync(InstallDir, plan, restartAfter: LaunchAfter, MakeProgress()));
    }

    [RelayCommand]
    private void BackToHome() => Screen = SetupScreen.Home;

    [RelayCommand]
    private void Uninstall()
    {
        string exe = Path.Combine(InstallDir, AppPaths.UninstallerExeName);
        try
        {
            if (File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch
        {
            // best effort — nothing actionable if the uninstaller can't be launched
        }
        Close();
    }

    [RelayCommand]
    private void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    private void Cancel() => Close();

    [RelayCommand]
    private void Finish() => Close();

    // ── Operation driver ──

    private Progress<ProgressReport> MakeProgress() => new(OnProgress);

    private async Task RunAsync(Task<OpResult> operation)
    {
        BuildTimeline();
        ResetLog();
        Screen = SetupScreen.Running;
        IsBusy = true;

        OpResult result;
        try
        {
            result = await operation;
        }
        catch (Exception ex)
        {
            result = OpResult.Fail(ex.Message);
        }

        IsBusy = false;
        ResultSuccess = result.Success;
        ResultMessage = result.Message;
        AppendLog(result.Message);

        if (result.Success)
            MarkAll(StepState.Done);
        else
            MarkActiveFailed();

        Screen = SetupScreen.Done;
    }

    private void OnProgress(ProgressReport report)
    {
        ProgressValue = report.Fraction * 100.0;
        ProgressStatus = report.Status;
        AppendLog(report.Status);

        if (!string.IsNullOrEmpty(report.Step))
            SetActiveStep(report.Step);
    }

    // ── Timeline ──

    private void BuildTimeline()
    {
        Steps.Clear();
        foreach ((string key, string label) in TimelineFor(_op))
            Steps.Add(new SetupStepItem(key, label));
    }

    private IEnumerable<(string Key, string Label)> TimelineFor(SetupMode mode) => mode switch
    {
        SetupMode.Update => new (string, string)[]
        {
            (SetupSteps.StopApp, "Stop running application"),
            (SetupSteps.Backup, "Back up current version"),
            (SetupSteps.Files, "Replace files"),
            (SetupSteps.Shortcuts, "Update shortcuts"),
            (SetupSteps.Autostart, "Update autostart"),
            (SetupSteps.Register, "Update registration"),
            (SetupSteps.Cleanup, "Clean up"),
            (SetupSteps.Finalize, "Finish")
        },
        SetupMode.Repair => RepairTimeline(),
        _ => new (string, string)[]
        {
            (SetupSteps.Prepare, "Prepare"),
            (SetupSteps.Files, "Install files"),
            (SetupSteps.Shortcuts, "Create shortcuts"),
            (SetupSteps.Autostart, "Configure autostart"),
            (SetupSteps.Register, "Register with Windows"),
            (SetupSteps.Finalize, "Finish")
        }
    };

    private (string, string)[] RepairTimeline()
    {
        List<(string, string)> s = new() { (SetupSteps.StopApp, "Stop running application") };
        if (RepairDeleteConfig)
            s.Add((SetupSteps.Config, "Back up & reset configuration"));
        if (RepairProgramFiles)
            s.Add((SetupSteps.Files, "Repair program files"));
        if (RepairPlugins || RepairDeleteConfig)
            s.Add((SetupSteps.Plugins, "Repair plugins"));
        if (RepairShortcuts)
            s.Add((SetupSteps.Shortcuts, "Repair shortcuts"));
        if (RepairAutostart)
            s.Add((SetupSteps.Autostart, "Repair autostart"));
        s.Add((SetupSteps.Register, "Update registration"));
        s.Add((SetupSteps.Finalize, "Finish"));
        return s.ToArray();
    }

    /// <summary>Marks <paramref name="key"/>'s step active and every earlier step done. Unknown keys
    /// (steps not surfaced in the curated timeline) leave the checkmarks unchanged.</summary>
    private void SetActiveStep(string key)
    {
        int idx = -1;
        for (int i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].Key == key)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
            return;

        for (int i = 0; i < Steps.Count; i++)
            Steps[i].State = i < idx ? StepState.Done : i == idx ? StepState.Active : StepState.Pending;
    }

    private void MarkAll(StepState state)
    {
        foreach (SetupStepItem step in Steps)
            step.State = state;
    }

    private void MarkActiveFailed()
    {
        foreach (SetupStepItem step in Steps)
        {
            if (step.State == StepState.Active)
            {
                step.State = StepState.Failed;
                break;
            }
        }
    }

    // ── Log ──

    private void ResetLog()
    {
        _log.Clear();
        _lastLogLine = "";
        LogText = "";
        ShowLog = false;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line == _lastLogLine)
            return;

        _lastLogLine = line;
        _log.AppendLine(line);
        LogText = _log.ToString();
    }

    // ── Helpers ──

    /// <summary>
    /// True when both version strings parse to the same <see cref="Version"/> (or, failing that, are
    /// equal as trimmed text). Used to steer an install-over-existing toward Repair rather than Update.
    /// </summary>
    private static bool IsSameVersion(string a, string b)
    {
        if (System.Version.TryParse(a, out System.Version? va) && System.Version.TryParse(b, out System.Version? vb))
            return va == vb;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void Close()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow?.Close();
    }
}
