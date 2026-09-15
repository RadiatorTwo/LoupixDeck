using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Folders;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.FolderPanel;

/// <summary>
/// The folder panel on the right of the main window (issue #249): the custom folder tree of the
/// active workspace, with create, rename, delete, move and open. One per device, like the apps and
/// commands panel.
/// </summary>
public sealed partial class FolderPanelViewModel : ViewModelBase
{
    private readonly LoupedeckConfig _config;
    private readonly ICustomFolderService _folders;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckLiveSController _controller;
    private readonly Services.Companion.ICompanionCoordinator _companions;
    private readonly string _scopeKey;

    public FolderPanelViewModel(LoupedeckConfig config, ICustomFolderService folders, IDialogService dialogService,
        LoupedeckLiveSController controller, Services.Companion.ICompanionCoordinator companions,
        Services.Companion.ICompanionContextSync companionSync, Registry.DeviceRegistry.DeviceInfo deviceInfo,
        Registry.ResolvedDevice resolved)
    {
        _config = config;
        _folders = folders;
        _dialogService = dialogService;
        _controller = controller;
        _companions = companions;
        _scopeKey = resolved?.ScopeKey ?? deviceInfo.Slug;

        _folders.StructureChanged += Rebuild;
        _config.PropertyChanged += OnConfigPropertyChanged;

        // A companion's tree is rewritten by the structure sync, which raises no model event of its own.
        companionSync.LinkedStructureChanged += key =>
        {
            if (string.Equals(key, _scopeKey, StringComparison.OrdinalIgnoreCase))
                Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
        };
        Rebuild();
    }

    /// <summary>
    /// False on a companion: its folder tree mirrors the master's and is edited there. Its folder
    /// layouts stay its own, so opening folders and linking them to keys still works.
    /// </summary>
    [ObservableProperty]
    public partial bool CanEditStructure { get; private set; } = true;

    /// <summary>Whether the panel is showing.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Name of the active workspace, the root of the tree.</summary>
    [ObservableProperty]
    public partial string WorkspaceName { get; private set; }

    /// <summary>True while no folder is open, so the workspace root is what the device shows.</summary>
    [ObservableProperty]
    public partial bool IsRootCurrent { get; private set; } = true;

    /// <summary>True when there is no folder yet and this device may create one.</summary>
    [ObservableProperty]
    public partial bool ShowEmptyHint { get; private set; }

    public ObservableCollection<FolderNodeViewModel> RootNodes { get; } = [];

    // ── Tree state ─────────────────────────────────────────────────────────

    private void OnConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoupedeckConfig.ActiveWorkspace):
                Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
                break;
            case nameof(LoupedeckConfig.FolderPath):
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCurrent);
                break;
            case nameof(Workspace.Name):
                Avalonia.Threading.Dispatcher.UIThread.Post(() => WorkspaceName = _config.ActiveWorkspace?.Name ?? string.Empty);
                break;
        }
    }

    /// <summary>Rebuilds the node tree from the model, keeping which folders were expanded.</summary>
    private void Rebuild()
    {
        HashSet<Guid> expanded = [.. RootNodes.SelectMany(static n => n.SelfAndDescendants())
            .Where(static n => n.IsExpanded).Select(static n => n.Folder.Id)];

        RootNodes.Clear();
        foreach (CustomFolder folder in _config.ActiveWorkspace?.Folders ?? [])
            if (folder != null)
                RootNodes.Add(BuildNode(folder, null, expanded));

        WorkspaceName = _config.ActiveWorkspace?.Name ?? string.Empty;
        CanEditStructure = _config.CompanionLink == null;
        ShowEmptyHint = RootNodes.Count == 0 && CanEditStructure;
        RefreshCurrent();
    }

    private static FolderNodeViewModel BuildNode(CustomFolder folder, FolderNodeViewModel parent, HashSet<Guid> expanded)
    {
        FolderNodeViewModel node = new(folder, parent) { IsExpanded = expanded.Contains(folder.Id) };
        foreach (CustomFolder child in folder.Children ?? [])
            if (child != null)
                node.Children.Add(BuildNode(child, node, expanded));
        return node;
    }

    /// <summary>Marks the open folder and the folders it was opened through, and expands down to it.</summary>
    private void RefreshCurrent()
    {
        IReadOnlyList<CustomFolder> path = _config.FolderPath;
        HashSet<Guid> inPath = [.. path.Select(static f => f.Id)];
        Guid? current = path.Count > 0 ? path[^1].Id : null;

        foreach (FolderNodeViewModel node in RootNodes.SelectMany(static n => n.SelfAndDescendants()))
        {
            node.IsCurrent = node.Folder.Id == current;
            node.IsInPath = !node.IsCurrent && inPath.Contains(node.Folder.Id);

            if (node.IsCurrent)
                for (FolderNodeViewModel parent = node.Parent; parent != null; parent = parent.Parent)
                    parent.IsExpanded = true;
        }

        IsRootCurrent = path.Count == 0;
    }

    private FolderNodeViewModel FindNode(CustomFolder folder)
        => RootNodes.SelectMany(static n => n.SelfAndDescendants()).FirstOrDefault(n => ReferenceEquals(n.Folder, folder));

    // ── Navigation ─────────────────────────────────────────────────────────

    /// <summary>Opens the folder on the device and in the editor, at its place in the tree.</summary>
    public Task OpenFolderAsync(FolderNodeViewModel node)
        => node == null ? Task.CompletedTask : _controller.PageManager.OpenFolder(node.Folder.Id, FolderOpenMode.Tree);

    [RelayCommand]
    private Task CloseFolders() => _controller.PageManager.CloseFolders();

    // ── Editing ────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task NewFolder() => CreateFolderAsync(null);

    [RelayCommand]
    private Task NewSubfolder(FolderNodeViewModel parent) => CreateFolderAsync(parent);

    private async Task CreateFolderAsync(FolderNodeViewModel parent)
    {
        if (!CanEditStructure) return;

        string name = await AskName("Prompt_NewFolderTitle", "Prompt_Create", Loc.Tr("FolderPanel_DefaultName"));
        if (string.IsNullOrWhiteSpace(name)) return;

        CustomFolder folder = _folders.Create(parent?.Folder, name);
        if (folder == null) return;

        _controller.SaveConfig();

        if (parent != null)
            FindNode(parent.Folder)?.IsExpanded = true;
    }

    [RelayCommand]
    private async Task Rename(FolderNodeViewModel node)
    {
        if (node == null || !CanEditStructure) return;

        string name = await AskName("Prompt_RenameFolderTitle", "Prompt_Rename", node.Folder.Name);
        if (string.IsNullOrWhiteSpace(name) || name == node.Folder.Name) return;

        _folders.Rename(node.Folder, name);
        _controller.SaveConfig();
    }

    [RelayCommand]
    private async Task Delete(FolderNodeViewModel node)
    {
        if (node == null || !CanEditStructure) return;

        FolderDeleteImpact impact = _folders.GetDeleteImpact(node.Folder);
        HashSet<Guid> removed = [.. node.Folder.SelfAndDescendants().Select(static f => f.Id)];
        IReadOnlyList<Services.Companion.CompanionLossEntry> losses = Services.Companion.CompanionImpact.ForFolders(
            _companions, _scopeKey, _config.ActiveWorkspaceId, removed);

        if (impact.HasContent || impact.Links > 0 || losses.Count > 0)
        {
            string message = Loc.Tr("Confirm_DeleteFolderMessage", node.Folder.Name, impact.Subfolders, impact.Links);
            if (losses.Count > 0)
                message += Environment.NewLine + Environment.NewLine + Loc.Tr("Confirm_CompanionPagesLost") +
                           Environment.NewLine + Services.Companion.CompanionImpact.Describe(losses);
            if (!await Ask("Confirm_DeleteFolderTitle", message, "Confirm_Delete")) return;
        }

        await _folders.Delete(node.Folder);
        _controller.SaveConfig();
    }

    /// <summary>True when the dragged folder may be moved below <paramref name="newParent"/> (null = top level).</summary>
    public bool CanMove(FolderNodeViewModel node, FolderNodeViewModel newParent)
        => CanEditStructure && node != null && _folders.CanMove(node.Folder, newParent?.Folder);

    /// <summary>Moves the folder below <paramref name="newParent"/> (null = top level) at <paramref name="index"/>.</summary>
    public void Move(FolderNodeViewModel node, FolderNodeViewModel newParent, int index)
    {
        if (node == null || !_folders.Move(node.Folder, newParent?.Folder, index)) return;

        if (newParent != null)
            FindNode(newParent.Folder)?.IsExpanded = true;
        _controller.SaveConfig();
    }

    // ── Dialogs ────────────────────────────────────────────────────────────

    private async Task<string> AskName(string titleKey, string confirmKey, string initialText)
    {
        TextInputDialogViewModel prompt = null;

        DialogResult result = await _dialogService.ShowDialogAsync<TextInputDialogViewModel, DialogResult>(vm =>
        {
            prompt = vm;
            vm.Configure(Loc.Tr(titleKey), Loc.Tr("Settings_FolderName"), Loc.Tr(confirmKey), initialText);
        });

        return result?.IsConfirmed == true && prompt != null ? prompt.Result?.Trim() : null;
    }

    private async Task<bool> Ask(string titleKey, string message, string confirmKey)
    {
        DialogResult result = await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(
                message,
                title: Loc.Tr(titleKey),
                confirmText: Loc.Tr(confirmKey),
                cancelText: Loc.Tr("Confirm_Cancel")));

        return result?.IsConfirmed == true;
    }
}
