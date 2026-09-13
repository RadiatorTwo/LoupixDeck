using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Commands behind the "⋮" menus next to the Profile and Workspace selectors in the main window
/// header. They act on the active profile / workspace, which is what the selectors show.
/// </summary>
public sealed class ProfileHeaderMenuViewModel : ViewModelBase
{
    private readonly IProfileEditingService _editing;
    private readonly IWorkspaceActivationService _activation;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckLiveSController _controller;

    public ProfileHeaderMenuViewModel(IProfileEditingService editing,
        IWorkspaceActivationService activation,
        IDialogService dialogService,
        LoupedeckLiveSController controller)
    {
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

    /// <summary>Re-evaluates which menu entries are enabled. Call after the tree was edited
    /// elsewhere (the Settings pane).</summary>
    public void Refresh()
    {
        RenameProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        NewWorkspaceCommand.NotifyCanExecuteChanged();
        RenameWorkspaceCommand.NotifyCanExecuteChanged();
        DeleteWorkspaceCommand.NotifyCanExecuteChanged();
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

        if (!await ConfirmDelete("Confirm_DeleteProfileTitle", "Confirm_DeleteProfileMessage", profile.Name))
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

        if (!await ConfirmDelete("Confirm_DeleteWorkspaceTitle", "Confirm_DeleteWorkspaceMessage", workspace.Name))
            return;

        if (await _editing.RemoveWorkspace(profile, workspace))
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

    private async Task<bool> ConfirmDelete(string titleKey, string messageKey, string itemName)
    {
        DialogResult result = await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(
                Loc.Tr(messageKey, itemName),
                title: Loc.Tr(titleKey),
                confirmText: Loc.Tr("Confirm_Delete"),
                cancelText: Loc.Tr("Confirm_Cancel")));

        return result?.IsConfirmed == true;
    }
}
