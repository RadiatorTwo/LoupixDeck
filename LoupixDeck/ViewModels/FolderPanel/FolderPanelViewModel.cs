using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
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
/// active workspace, with create, rename, delete, move, search and open. One per device, like the
/// apps and commands panel.
/// </summary>
/// <remarks>
/// The tree is shown as a flat row list (<see cref="VisibleRows"/>): expansion and the search
/// filter decide which nodes are in it, and the rows carry their depth.
/// </remarks>
public sealed partial class FolderPanelViewModel : ViewModelBase
{
    private readonly LoupedeckConfig _config;
    private readonly ICustomFolderService _folders;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckLiveSController _controller;
    private readonly Services.Companion.ICompanionCoordinator _companions;
    private readonly string _scopeKey;

    // Folders are expanded unless the user collapsed them, so new folders show their children.
    private readonly HashSet<Guid> _collapsed = [];

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
                Dispatcher.UIThread.Post(Rebuild);
        };
        Rebuild();
    }

    /// <summary>
    /// False on a companion: its folder tree mirrors the master's and is edited there. Its folder
    /// layouts stay its own, so opening folders and linking them to keys still works.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteCurrent))]
    public partial bool CanEditStructure { get; private set; } = true;

    /// <summary>Whether the panel is showing.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Name of the active workspace, the root of the tree.</summary>
    [ObservableProperty]
    public partial string WorkspaceName { get; private set; }

    /// <summary>True while no folder is open, so the workspace root is what the device shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteCurrent))]
    public partial bool IsRootCurrent { get; private set; } = true;

    /// <summary>True when the open folder may be deleted from the header.</summary>
    public bool CanDeleteCurrent => CanEditStructure && !IsRootCurrent;

    /// <summary>True when there is no folder yet and this device may create one.</summary>
    [ObservableProperty]
    public partial bool ShowEmptyHint { get; private set; }

    /// <summary>True when a search is active and nothing matches it.</summary>
    [ObservableProperty]
    public partial bool ShowNoMatches { get; private set; }

    /// <summary>Live filter on the folder names.</summary>
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    /// <summary>Number of folders in the workspace, shown in the header.</summary>
    [ObservableProperty]
    public partial string Counter { get; private set; } = string.Empty;

    /// <summary>Footer path of the open folder, starting at the workspace, e.g. "Home / Media".</summary>
    [ObservableProperty]
    public partial string PathLabel { get; private set; } = string.Empty;

    public ObservableCollection<FolderNodeViewModel> RootNodes { get; } = [];

    /// <summary>The folder rows currently shown, in tree order.</summary>
    public ObservableCollection<FolderNodeViewModel> VisibleRows { get; } = [];

    partial void OnSearchQueryChanged(string value) => RefreshRows();

    // ── Tree state ─────────────────────────────────────────────────────────

    private void OnConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoupedeckConfig.ActiveWorkspace):
                Dispatcher.UIThread.Post(Rebuild);
                break;
            case nameof(LoupedeckConfig.FolderPath):
                Dispatcher.UIThread.Post(RefreshCurrent);
                break;
            case nameof(Workspace.Name):
                Dispatcher.UIThread.Post(() =>
                {
                    WorkspaceName = _config.ActiveWorkspace?.Name ?? string.Empty;
                    RefreshPath();
                });
                break;
        }
    }

    /// <summary>Rebuilds the node tree from the model, keeping which folders were collapsed.</summary>
    private void Rebuild()
    {
        RootNodes.Clear();
        foreach (CustomFolder folder in _config.ActiveWorkspace?.Folders ?? [])
            if (folder != null)
                RootNodes.Add(BuildNode(folder, null));

        WorkspaceName = _config.ActiveWorkspace?.Name ?? string.Empty;
        CanEditStructure = _config.CompanionLink == null;
        ShowEmptyHint = RootNodes.Count == 0 && CanEditStructure;
        RefreshCurrent();
    }

    private FolderNodeViewModel BuildNode(CustomFolder folder, FolderNodeViewModel parent)
    {
        FolderNodeViewModel node = new(folder, parent) { IsExpanded = !_collapsed.Contains(folder.Id) };
        foreach (CustomFolder child in folder.Children ?? [])
            if (child != null)
                node.Children.Add(BuildNode(child, node));
        return node;
    }

    private IEnumerable<FolderNodeViewModel> AllNodes => RootNodes.SelectMany(static n => n.SelfAndDescendants());

    /// <summary>Marks the open folder and the folders it was opened through, and expands down to it.</summary>
    private void RefreshCurrent()
    {
        IReadOnlyList<CustomFolder> path = _config.FolderPath;
        HashSet<Guid> inPath = [.. path.Select(static f => f.Id)];
        Guid? current = path.Count > 0 ? path[^1].Id : null;

        foreach (FolderNodeViewModel node in AllNodes)
        {
            node.IsCurrent = node.Folder.Id == current;
            node.IsInPath = !node.IsCurrent && inPath.Contains(node.Folder.Id);

            if (node.IsCurrent)
                for (FolderNodeViewModel parent = node.Parent; parent != null; parent = parent.Parent)
                    SetExpanded(parent, true);
        }

        IsRootCurrent = path.Count == 0;
        RefreshCounts();
        RefreshPath();
        RefreshRows();
    }

    /// <summary>Recounts the configured keys of every folder; a folder's keys change while it is open.</summary>
    private void RefreshCounts()
    {
        int folders = 0;
        foreach (FolderNodeViewModel node in AllNodes)
        {
            int count = node.Folder.Layout?.TouchButtons
                .Count(static b => b != null && !b.IsFolderBackSlot && !ButtonSnapshot.IsEmpty(b)) ?? 0;
            node.ActionCount = count > 0 ? count.ToString(CultureInfo.CurrentCulture) : string.Empty;
            folders++;
        }

        Counter = folders > 0 ? folders.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }

    private void RefreshPath()
    {
        IEnumerable<string> names = [WorkspaceName, .. _config.FolderPath.Select(static f => f.Name)];
        PathLabel = string.Join(" / ", names);
    }

    /// <summary>
    /// Brings <see cref="VisibleRows"/> in line with expansion and search. While a search is active,
    /// every branch leading to a match is shown expanded. Rows that stay are not re-created, so an
    /// open rename box survives a refresh.
    /// </summary>
    private void RefreshRows()
    {
        string query = SearchQuery?.Trim() ?? string.Empty;
        bool searching = query.Length > 0;
        List<FolderNodeViewModel> rows = [];

        void Walk(FolderNodeViewModel node, int depth)
        {
            if (searching && !node.SelfAndDescendants().Any(n =>
                    (n.Folder.Name ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                return;

            node.Depth = depth;
            rows.Add(node);

            if (searching || node.IsExpanded)
                foreach (FolderNodeViewModel child in node.Children)
                    Walk(child, depth + 1);
        }

        foreach (FolderNodeViewModel root in RootNodes)
            Walk(root, 1);

        ShowNoMatches = searching && rows.Count == 0 && RootNodes.Count > 0;

        if (rows.SequenceEqual(VisibleRows)) return;

        // Keep the rows in place that are still there, so their containers survive.
        for (int i = VisibleRows.Count - 1; i >= 0; i--)
            if (!rows.Contains(VisibleRows[i]))
                VisibleRows.RemoveAt(i);

        for (int i = 0; i < rows.Count; i++)
        {
            int existing = VisibleRows.IndexOf(rows[i]);
            if (existing == i) continue;
            if (existing >= 0)
                VisibleRows.Move(existing, i);
            else
                VisibleRows.Insert(i, rows[i]);
        }
    }

    private void SetExpanded(FolderNodeViewModel node, bool expanded)
    {
        node.IsExpanded = expanded;
        if (expanded)
            _collapsed.Remove(node.Folder.Id);
        else
            _collapsed.Add(node.Folder.Id);
    }

    private FolderNodeViewModel FindNode(CustomFolder folder)
        => AllNodes.FirstOrDefault(n => ReferenceEquals(n.Folder, folder));

    private FolderNodeViewModel CurrentNode => AllNodes.FirstOrDefault(static n => n.IsCurrent);

    [RelayCommand]
    private void ToggleExpanded(FolderNodeViewModel node)
    {
        if (node == null || !node.HasChildren) return;

        SetExpanded(node, !node.IsExpanded);
        RefreshRows();
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    /// <summary>Opens the folder on the device and in the editor, at its place in the tree.</summary>
    public Task OpenFolderAsync(FolderNodeViewModel node)
        => node == null ? Task.CompletedTask : _controller.PageManager.OpenFolder(node.Folder.Id, FolderOpenMode.Tree);

    [RelayCommand]
    private Task CloseFolders() => _controller.PageManager.CloseFolders();

    // ── Editing ────────────────────────────────────────────────────────────

    /// <summary>Creates a folder inside the open one (or at the top level), opens it and starts renaming it.</summary>
    [RelayCommand]
    private Task NewFolder() => CreateFolderAsync(CurrentNode);

    [RelayCommand]
    private Task NewSubfolder(FolderNodeViewModel parent) => CreateFolderAsync(parent);

    private async Task CreateFolderAsync(FolderNodeViewModel parent)
    {
        if (!CanEditStructure) return;

        if (parent != null)
            _collapsed.Remove(parent.Folder.Id);

        SearchQuery = string.Empty;

        CustomFolder folder = _folders.Create(parent?.Folder, UniqueDefaultName(parent?.Folder));
        if (folder == null) return;

        _controller.SaveConfig();
        await _controller.PageManager.OpenFolder(folder.Id, FolderOpenMode.Tree);

        // After the posted refreshes of the navigation, so the new row is in the list.
        Dispatcher.UIThread.Post(() => StartRename(FindNode(folder)), DispatcherPriority.Background);
    }

    private string UniqueDefaultName(CustomFolder parent)
    {
        string baseName = Loc.Tr("FolderPanel_DefaultName");
        IEnumerable<CustomFolder> siblings = parent?.Children ?? _config.ActiveWorkspace?.Folders ?? [];
        HashSet<string> taken = new(siblings.Where(static f => f != null).Select(static f => f.Name),
            StringComparer.CurrentCultureIgnoreCase);

        if (!taken.Contains(baseName)) return baseName;

        int n = 2;
        while (taken.Contains($"{baseName} {n}")) n++;
        return $"{baseName} {n}";
    }

    [RelayCommand]
    private void Rename(FolderNodeViewModel node) => StartRename(node);

    /// <summary>Opens the in-place rename box on the row.</summary>
    public void StartRename(FolderNodeViewModel node)
    {
        if (node == null || !CanEditStructure) return;

        foreach (FolderNodeViewModel other in AllNodes.Where(static n => n.IsEditing))
            if (!ReferenceEquals(other, node))
                other.RenameSession = null;

        node.RenameSession = new FolderRenameSession(node, node.Folder.Name);
    }

    /// <summary>Ends <paramref name="session"/> and applies <paramref name="text"/> unless it is blank or unchanged.</summary>
    public void CommitRename(FolderRenameSession session, string text)
    {
        FolderNodeViewModel node = session?.Node;
        if (node == null || !ReferenceEquals(node.RenameSession, session)) return;

        node.RenameSession = null;

        string name = text?.Trim();
        if (string.IsNullOrEmpty(name) || name == node.Folder.Name) return;

        _folders.Rename(node.Folder, name);
        _controller.SaveConfig();
    }

    /// <summary>Ends <paramref name="session"/> without renaming.</summary>
    public void CancelRename(FolderRenameSession session)
    {
        if (session?.Node != null && ReferenceEquals(session.Node.RenameSession, session))
            session.Node.RenameSession = null;
    }

    [RelayCommand]
    private Task DeleteCurrent() => Delete(CurrentNode);

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
        if (node == null) return;

        if (newParent != null)
            _collapsed.Remove(newParent.Folder.Id);

        if (!_folders.Move(node.Folder, newParent?.Folder, index)) return;

        _controller.SaveConfig();
    }

    // ── Dialogs ────────────────────────────────────────────────────────────

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
