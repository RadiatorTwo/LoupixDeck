using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.Profiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Commands behind the "⋮" menus next to the Profile and Workspace selectors in the main window
/// header. They act on the active profile / workspace, which is what the selectors show.
/// </summary>
public sealed class ProfileHeaderMenuViewModel : ViewModelBase
{
    private readonly LoupedeckConfig _config;
    private readonly IProfileEditingService _editing;
    private readonly IWorkspaceActivationService _activation;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckLiveSController _controller;

    public ProfileHeaderMenuViewModel(LoupedeckConfig config,
        IProfileEditingService editing,
        IWorkspaceActivationService activation,
        IDialogService dialogService,
        LoupedeckLiveSController controller)
    {
        _config = config;
        _editing = editing;
        _activation = activation;
        _dialogService = dialogService;
        _controller = controller;

        // Whether delete is allowed depends on the active profile's workspace count, so re-evaluate
        // whenever the context changes. The events can arrive off the UI thread.
        _activation.ActiveProfileChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        _activation.ActiveWorkspaceChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    public IAsyncRelayCommand NewProfileCommand => field ??= Relay.Create(NewProfile);
    public IAsyncRelayCommand RenameProfileCommand => field ??= Relay.Create(RenameProfile,
        () => _activation.ActiveProfile != null);
    public IAsyncRelayCommand DeleteProfileCommand => field ??= Relay.Create(DeleteProfile,
        () => _editing.CanRemoveProfile(_activation.ActiveProfile));

    public IAsyncRelayCommand NewWorkspaceCommand => field ??= Relay.Create(NewWorkspace,
        () => _activation.ActiveProfile != null);
    public IAsyncRelayCommand RenameWorkspaceCommand => field ??= Relay.Create(RenameWorkspace,
        () => _activation.ActiveWorkspace != null);
    public IAsyncRelayCommand DeleteWorkspaceCommand => field ??= Relay.Create(DeleteWorkspace,
        () => _editing.CanRemoveWorkspace(_activation.ActiveProfile, _activation.ActiveWorkspace));

    /// <summary>Linking needs foreground-app detection, which exists only on Windows and Linux.</summary>
    public bool IsAppLinkingSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public IAsyncRelayCommand LinkApplicationCommand => field ??= Relay.Create(LinkApplication,
        () => _activation.ActiveProfile != null);
    public IAsyncRelayCommand UnlinkApplicationCommand => field ??= Relay.Create(UnlinkApplication,
        () => _activation.ActiveProfile is { } profile
              && ProfileAppLink.FindLinkedProcessName(_config.ContextRules, profile.Id).Length > 0);

    /// <summary>Re-evaluates which menu entries are enabled. Call after the tree was edited
    /// elsewhere (the Settings pane).</summary>
    public void Refresh()
    {
        RenameProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        NewWorkspaceCommand.NotifyCanExecuteChanged();
        RenameWorkspaceCommand.NotifyCanExecuteChanged();
        DeleteWorkspaceCommand.NotifyCanExecuteChanged();
        LinkApplicationCommand.NotifyCanExecuteChanged();
        UnlinkApplicationCommand.NotifyCanExecuteChanged();
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
    }

    private async Task DeleteProfile()
    {
        Profile profile = _activation.ActiveProfile;
        if (!_editing.CanRemoveProfile(profile)) return;

        if (!await Ask("Confirm_DeleteProfileTitle", Loc.Tr("Confirm_DeleteProfileMessage", profile.Name),
                "Confirm_Delete"))
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

        if (!await Ask("Confirm_DeleteWorkspaceTitle", Loc.Tr("Confirm_DeleteWorkspaceMessage", workspace.Name),
                "Confirm_Delete"))
            return;

        if (await _editing.RemoveWorkspace(profile, workspace))
            _controller.SaveConfig();

        Refresh();
    }

    private async Task LinkApplication()
    {
        Profile profile = _activation.ActiveProfile;
        if (profile == null) return;

        AppPickerRequest request = new();
        DialogResult picked = await _dialogService.ShowDialogAsync<AppPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (picked is not { IsConfirmed: true } || request.SelectedApp == null) return;

        InstalledApp app = request.SelectedApp;

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
