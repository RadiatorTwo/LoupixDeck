using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Companion;
using LoupixDeck.Services.Portable;
using LoupixDeck.Services.Profiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Commands behind the "⋮" menus next to the Profile and Workspace selectors in the main window
/// header. They act on the active profile / workspace, which is what the selectors show. On a
/// companion the master owns both, so the selectors and every entry here are disabled.
/// </summary>
public sealed class ProfileHeaderMenuViewModel : ViewModelBase
{
    private readonly LoupedeckConfig _config;
    private readonly IProfileEditingService _editing;
    private readonly IWorkspaceActivationService _activation;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckLiveSController _controller;
    private readonly ICompanionCoordinator _companions;
    private readonly ResolvedDevice _device;

    public ProfileHeaderMenuViewModel(LoupedeckConfig config,
        IProfileEditingService editing,
        IWorkspaceActivationService activation,
        IDialogService dialogService,
        LoupedeckLiveSController controller,
        ICompanionCoordinator companions,
        ResolvedDevice device)
    {
        _config = config;
        _editing = editing;
        _activation = activation;
        _dialogService = dialogService;
        _controller = controller;
        _companions = companions;
        _device = device;

        // Whether delete is allowed depends on the active profile's workspace count, so re-evaluate
        // whenever the context changes. The events can arrive off the UI thread.
        _activation.ActiveProfileChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        _activation.ActiveWorkspaceChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        // Joining or leaving a group locks or unlocks the whole menu. The coordinator is a root
        // singleton that outlives this view model, as the device provider does.
        _companions.GroupsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        // The lock hint says whether the master is connected.
        _companions.DeviceOnlineStateChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCompanionHint);
    }

    /// <summary>False on a companion whose profile and workspace follow its master. A paused
    /// companion switches on its own.</summary>
    public bool CanSwitchContext => !_companions.IsFollowingMaster(_device.ScopeKey);

    /// <summary>False on any companion: its profiles and workspaces mirror the master's, paused or not.</summary>
    public bool CanEditStructure => !_companions.IsCompanion(_device.ScopeKey);

    /// <summary>True on a companion, paused or not: the header then shows whom it follows.</summary>
    public bool IsCompanionDevice => !CanEditStructure;

    /// <summary>Scope key of the master this companion follows, or null.</summary>
    public string MasterKey => _companions.GetMasterKey(_device.ScopeKey);

    /// <summary>"Follows Loupedeck Live S", or null when this device is not a companion.</summary>
    public string FollowsMasterText => CompanionStatusText.FollowsMaster(_companions, _device.ScopeKey);

    /// <summary>Tooltip of the locked selectors and the header hint; null (no tooltip) when not locked.</summary>
    public string CompanionLockExplanation => CompanionStatusText.LockExplanation(_companions, _device.ScopeKey);

    public IAsyncRelayCommand NewProfileCommand => field ??= Relay.Create(NewProfile, () => CanEditStructure);
    public IAsyncRelayCommand RenameProfileCommand => field ??= Relay.Create(RenameProfile,
        () => CanEditStructure && _activation.ActiveProfile != null);
    public IAsyncRelayCommand DeleteProfileCommand => field ??= Relay.Create(DeleteProfile,
        () => CanEditStructure && _editing.CanRemoveProfile(_activation.ActiveProfile));

    public IAsyncRelayCommand NewWorkspaceCommand => field ??= Relay.Create(NewWorkspace,
        () => CanEditStructure && _activation.ActiveProfile != null);
    public IAsyncRelayCommand RenameWorkspaceCommand => field ??= Relay.Create(RenameWorkspace,
        () => CanEditStructure && _activation.ActiveWorkspace != null);
    public IAsyncRelayCommand DeleteWorkspaceCommand => field ??= Relay.Create(DeleteWorkspace,
        () => CanEditStructure && _editing.CanRemoveWorkspace(_activation.ActiveProfile, _activation.ActiveWorkspace));

    // Export is allowed on a companion too: the pages in its mirrors are its own.
    public IAsyncRelayCommand ExportProfileCommand => field ??= Relay.Create(
        () => Export(_activation.ActiveProfile is { } profile ? ProfileExportRequest.ForProfile(profile) : null),
        () => _activation.ActiveProfile != null);
    public IAsyncRelayCommand ExportWorkspaceCommand => field ??= Relay.Create(
        () => Export(_activation.ActiveWorkspace is { } workspace ? ProfileExportRequest.ForWorkspace(workspace) : null),
        () => _activation.ActiveWorkspace != null);

    // A companion's profiles and workspaces mirror its master's, so it cannot import them.
    public IAsyncRelayCommand ImportPackageCommand => field ??= Relay.Create(ImportPackage, () => CanEditStructure);
    public IAsyncRelayCommand ImportLoupedeckCommand => field ??= Relay.Create(ImportLoupedeck, () => CanEditStructure);

    /// <summary>Linking needs foreground-app detection, which exists only on Windows and Linux.</summary>
    public bool IsAppLinkingSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public IAsyncRelayCommand LinkApplicationCommand => field ??= Relay.Create(LinkApplication,
        () => CanEditStructure && _activation.ActiveProfile != null);
    public IAsyncRelayCommand UnlinkApplicationCommand => field ??= Relay.Create(UnlinkApplication,
        () => CanEditStructure && _activation.ActiveProfile is { } profile
              && ProfileAppLink.FindLinkedProcessName(_config.ContextRules, profile.Id).Length > 0);

    /// <summary>Raised after an application link was added or removed, or the active profile was
    /// renamed — everything that other views naming the active profile's link have to redraw for.</summary>
    public event Action LinkChanged;

    /// <summary>Re-evaluates which menu entries are enabled. Call after the tree was edited
    /// elsewhere (the Settings pane).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(CanSwitchContext));
        OnPropertyChanged(nameof(CanEditStructure));
        OnPropertyChanged(nameof(IsCompanionDevice));
        OnPropertyChanged(nameof(MasterKey));
        RefreshCompanionHint();
        NewProfileCommand.NotifyCanExecuteChanged();
        RenameProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        NewWorkspaceCommand.NotifyCanExecuteChanged();
        RenameWorkspaceCommand.NotifyCanExecuteChanged();
        DeleteWorkspaceCommand.NotifyCanExecuteChanged();
        LinkApplicationCommand.NotifyCanExecuteChanged();
        UnlinkApplicationCommand.NotifyCanExecuteChanged();
        ExportProfileCommand.NotifyCanExecuteChanged();
        ExportWorkspaceCommand.NotifyCanExecuteChanged();
        ImportPackageCommand.NotifyCanExecuteChanged();
        ImportLoupedeckCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Opens the import preview. The header has no status line, so a failed import or one with
    /// notes is reported in a notice; a clean import simply shows up in the selectors.
    /// </summary>
    private async Task ImportPackage()
    {
        ProfilePackageResult result = await ProfileImportViewModel.ShowAsync(_dialogService);
        if (result == null)
            return;

        Refresh();

        if (result.Success && result.Warnings.Count == 0)
            return;

        string message = string.Join(Environment.NewLine, [result.Message, .. result.Warnings]);
        await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(message, title: Loc.Tr("MainWindow_ImportPackageResultTitle"),
                confirmText: Loc.Tr("Confirm_Ok"), showCancel: false));
    }

    /// <summary>Opens the Loupedeck import preview and reports the outcome in a notice.</summary>
    private async Task ImportLoupedeck()
    {
        string message = await LoupedeckImportViewModel.ShowAsync(_dialogService);
        if (message == null)
            return;

        Refresh();

        await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(message, title: Loc.Tr("LoupedeckImport_ResultTitle"),
                confirmText: Loc.Tr("Confirm_Ok"), showCancel: false));
    }

    /// <summary>Opens the export dialog; it reports failures and notes itself.</summary>
    private Task Export(ProfileExportRequest request) =>
        request == null ? Task.CompletedTask : ProfileExportViewModel.ShowAsync(_dialogService, request);

    private void RefreshCompanionHint()
    {
        OnPropertyChanged(nameof(FollowsMasterText));
        OnPropertyChanged(nameof(CompanionLockExplanation));
    }

    private async Task NewProfile()
    {
        string name = await AskName("Prompt_NewProfileTitle", "Settings_ProfileName", "Prompt_Create", null);
        if (name == null) return;

        Profile profile = _editing.AddProfile(name);
        await _activation.ActivateProfile(profile.Id);
        _controller.SaveConfig();
        Refresh();
    }

    private async Task RenameProfile()
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null) return;

        string name = await AskName("Prompt_RenameProfileTitle", "Settings_ProfileName", "Prompt_Rename", profile.Name);
        if (name == null || name == profile.Name) return;

        profile.Name = name;
        _controller.SaveConfig();
        LinkChanged?.Invoke();
    }

    private async Task DeleteProfile()
    {
        Profile profile = _activation.ActiveProfile;
        if (!_editing.CanRemoveProfile(profile)) return;

        string message = WithCompanionLosses(Loc.Tr("Confirm_DeleteProfileMessage", profile.Name),
            CompanionImpact.ForProfile(_companions, _device.ScopeKey, profile.Id));
        if (!await Ask("Confirm_DeleteProfileTitle", message, "Confirm_Delete"))
            return;

        if (await _editing.RemoveProfile(profile))
            _controller.SaveConfig();

        Refresh();
    }

    private async Task NewWorkspace()
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null) return;

        string name = await AskName("Prompt_NewWorkspaceTitle", "Settings_WorkspaceName", "Prompt_Create", null);
        if (name == null) return;

        Workspace workspace = _editing.AddWorkspace(profile, name);
        await _activation.ActivateWorkspace(workspace.Id);
        _controller.SaveConfig();
        Refresh();
    }

    private async Task RenameWorkspace()
    {
        Workspace workspace = _activation.ActiveWorkspace;
        if (workspace == null) return;

        string name = await AskName("Prompt_RenameWorkspaceTitle", "Settings_WorkspaceName", "Prompt_Rename", workspace.Name);
        if (name == null || name == workspace.Name) return;

        workspace.Name = name;
        _controller.SaveConfig();
    }

    private async Task DeleteWorkspace()
    {
        Profile profile = _activation.ActiveProfile;
        Workspace workspace = _activation.ActiveWorkspace;
        if (!_editing.CanRemoveWorkspace(profile, workspace)) return;

        string message = WithCompanionLosses(Loc.Tr("Confirm_DeleteWorkspaceMessage", workspace.Name),
            CompanionImpact.ForWorkspace(_companions, _device.ScopeKey, workspace.Id));
        if (!await Ask("Confirm_DeleteWorkspaceTitle", message, "Confirm_Delete"))
            return;

        if (await _editing.RemoveWorkspace(profile, workspace))
            _controller.SaveConfig();

        Refresh();
    }

    private async Task LinkApplication()
    {
        if (_activation.ActiveProfile == null) return;

        AppPickerRequest request = new();
        DialogResult picked = await _dialogService.ShowDialogAsync<AppPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (picked is not { IsConfirmed: true } || request.SelectedApp == null) return;

        await LinkApplicationAsync(request.SelectedApp);
    }

    /// <summary>
    /// Links <paramref name="app"/> to the active profile: rejects apps without a usable process name,
    /// resolves conflicting rules after asking, writes the plain rule, offers to turn on automatic
    /// switching, and saves. Shared by the header's picker entry and the panel's context menu.
    /// </summary>
    public async Task LinkApplicationAsync(InstalledApp app)
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null || app == null) return;

        if (!ProfileAppLink.CanLinkProcess(app.ProcessName))
        {
            await Ask("AppLink_NoProcessTitle", Loc.Tr("AppLink_NoProcessMessage", app.Name), "Confirm_Ok");
            return;
        }

        IReadOnlyList<ContextRule> conflicts =
            ProfileAppLink.FindConflictingRules(_config.ContextRules, profile.Id, app.ProcessName);

        if (conflicts.Count > 0)
        {
            if (!await Ask("Confirm_LinkAppConflictTitle",
                    Loc.Tr("Confirm_LinkAppConflictMessage", app.Name, profile.Name), "Confirm_LinkHere"))
                return;

            foreach (ContextRule conflict in conflicts)
                _config.ContextRules.Remove(conflict);
        }

        ProfileAppLink.Link(_config.ContextRules, profile.Id, app.ProcessName);
        _controller.SaveConfig();

        if (!_config.AppSwitchingEnabled
            && await Ask("Confirm_EnableAppSwitchingTitle",
                Loc.Tr("Confirm_EnableAppSwitchingMessage", app.Name), "Confirm_TurnOn", "Confirm_NotNow"))
        {
            _config.AppSwitchingEnabled = true;
            _controller.SaveConfig();
        }

        Refresh();
        LinkChanged?.Invoke();
    }

    private async Task UnlinkApplication()
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null) return;

        string process = ProfileAppLink.FindLinkedProcessName(_config.ContextRules, profile.Id);
        if (process.Length == 0) return;

        if (!await Ask("Confirm_UnlinkAppTitle",
                Loc.Tr("Confirm_UnlinkAppMessage", profile.Name, process), "Confirm_Remove"))
            return;

        ProfileAppLink.Unlink(_config.ContextRules, profile.Id);
        _controller.SaveConfig();
        Refresh();
        LinkChanged?.Invoke();
    }

    /// <summary>
    /// Removes the active profile's plain link, but only when it is <paramref name="app"/>'s process.
    /// A menu opened before the link changed elsewhere must not remove another application's link.
    /// </summary>
    public async Task UnlinkApplicationAsync(InstalledApp app)
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null || app == null) return;

        string linked = ProfileAppLink.FindLinkedProcessName(_config.ContextRules, profile.Id);
        if (linked.Length == 0
            || !string.Equals(linked, ContextRuleMatcher.Normalize(app.ProcessName), StringComparison.OrdinalIgnoreCase))
            return;

        await UnlinkApplication();
    }

    /// <summary>Shows the name prompt. Returns the trimmed name, or null when cancelled.</summary>
    private async Task<string> AskName(string titleKey, string placeholderKey, string confirmKey, string initialText)
    {
        TextInputDialogViewModel prompt = null;

        DialogResult result = await _dialogService.ShowDialogAsync<TextInputDialogViewModel, DialogResult>(vm =>
        {
            prompt = vm;
            vm.Configure(Loc.Tr(titleKey), Loc.Tr(placeholderKey), Loc.Tr(confirmKey), initialText);
        });

        return result?.IsConfirmed == true && prompt != null ? prompt.Result : null;
    }

    /// <summary>Shows the confirm dialog. True only when the confirm button was used.</summary>
    /// <summary>Appends which companions lose their own pages, when any do.</summary>
    private static string WithCompanionLosses(string message, IReadOnlyList<CompanionLossEntry> losses) =>
        losses.Count == 0
            ? message
            : message + Environment.NewLine + Environment.NewLine + Loc.Tr("Confirm_CompanionPagesLost") +
              Environment.NewLine + CompanionImpact.Describe(losses);

    private async Task<bool> Ask(string titleKey, string message, string confirmKey, string cancelKey = "Confirm_Cancel")
    {
        DialogResult result = await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(
                message,
                title: Loc.Tr(titleKey),
                confirmText: Loc.Tr(confirmKey),
                cancelText: Loc.Tr(cancelKey)));

        return result?.IsConfirmed == true;
    }
}
