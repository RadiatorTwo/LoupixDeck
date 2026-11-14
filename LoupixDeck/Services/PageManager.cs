using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Services.Folders;

namespace LoupixDeck.Services;

public interface IPageManager
{
    int PreviousTouchPageIndex { get; set; }
    int CurrentTouchPageIndex { get; set; }
    int CurrentRotaryPageIndex { get; set; }
    ObservableCollection<TouchButtonPage> TouchButtonPages { get; }
    ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; }
    RotaryButtonPage CurrentRotaryButtonPage { get; }
    TouchButtonPage CurrentTouchButtonPage { get; }
    SimpleButton[] SimpleButtons { get; }

    /// <summary>True when the active device pages its dial columns independently (side strips).</summary>
    bool HasIndependentRotarySides { get; }

    /// <summary>
    /// Number of knobs a single side page holds on a side-strip device (3 of the Razer's 6).
    /// Exposed so callers that build or resize a side page — the profile package importer —
    /// use the same figure the page factory does instead of re-deriving it.
    /// </summary>
    int SideRotaryButtonCount { get; }

    void NextRotaryPage();
    void PreviousRotaryPage();
    void ApplyRotaryPage(int pageIndex, bool init = false);

    // Side-aware paging — used by devices with side strips (Razer). RotarySide.Both
    // falls back to the single shared list (Live S and legacy behaviour).
    ObservableCollection<RotaryButtonPage> GetRotaryPages(RotarySide side);
    RotaryButtonPage GetCurrentRotaryPage(RotarySide side);
    int GetCurrentRotaryPageIndex(RotarySide side);

    /// <summary>Returns the page <paramref name="direction"/> steps from the side's
    /// current page (wrapping), without changing the active page — used to pre-render
    /// the neighbour for a swipe animation. Returns null when the side has ≤1 page.</summary>
    RotaryButtonPage PeekRotaryPage(RotarySide side, int direction);
    void NextRotaryPage(RotarySide side);
    void PreviousRotaryPage(RotarySide side);
    void ApplyRotaryPage(RotarySide side, int pageIndex, bool init = false);
    void AddRotaryButtonPage(RotarySide side, bool init = false);
    void DeleteRotaryButtonPage(RotarySide side);

    /// <summary>Inserts a copy of the side's page at <paramref name="pageIndex"/> right after it
    /// and makes the copy current.</summary>
    void DuplicateRotaryButtonPage(RotarySide side, int pageIndex);

    Task NextTouchPage();
    Task PreviousTouchPage();

    /// <summary>Switches the active touch page. When <paramref name="draw"/> is false the page
    /// state is committed (index, selection, <see cref="OnTouchPageChanged"/>) but the buttons
    /// are NOT drawn to the device and the page-name overlay is suppressed — used by the
    /// touch-page slide animation, which renders the incoming page itself and paints the final
    /// frame. Defaults to true (the normal immediate redraw).</summary>
    Task ApplyTouchPage(int pageIndex, bool init = false, bool draw = true);

    void AddRotaryButtonPage(bool init = false);
    void DeleteRotaryButtonPage();
    Task AddTouchButtonPage(bool init = false);
    Task DeleteTouchButtonPage();

    /// <summary>Duplicates the current rotary page (on a side-strip device, the current page of
    /// each column, so both sides stay in step).</summary>
    void DuplicateRotaryButtonPage();

    /// <summary>Inserts a copy of the touch page at <paramref name="pageIndex"/> right after it
    /// and makes the copy current.</summary>
    Task DuplicateTouchButtonPage(int pageIndex);

    void RefreshTouchButtons();
    void RefreshSimpleButtons();

    /// <summary>Fired when a rotary page changes: (side, previousIndex, newIndex).</summary>
    event Action<RotarySide, int, int> OnRotaryPageChanged;
    event Action<int, int> OnTouchPageChanged;

    /// <summary>
    /// Fired whenever the touch layout shown changes, whatever the reason. Listeners that only care
    /// about which buttons are on screen subscribe here instead of <see cref="OnTouchPageChanged"/>,
    /// whose indices describe pages only.
    /// </summary>
    event Action TouchLayoutChanged;

    /// <summary>
    /// Fired once every key of the touch layout on screen has been painted to the device in full
    /// (a page or folder switch, start-up, wake, the end of a page slide). <see cref="TouchLayoutChanged"/>
    /// fires before that paint, so a value that arrives in between can be painted over by it; listeners
    /// that push per-key content repaint those keys here.
    /// </summary>
    event Action TouchLayoutDrawn;

    /// <summary>Raises <see cref="TouchLayoutDrawn"/> after a caller outside the page manager painted the whole layout.</summary>
    void NotifyTouchLayoutDrawn();

    // --- Custom folders (issue #249) -----------------------------------------

    /// <summary>
    /// Opens a folder of the active workspace. <see cref="FolderOpenMode.Tree"/> opens it at its
    /// place in the folder tree (panel, breadcrumbs); <see cref="FolderOpenMode.Push"/> opens it on
    /// top of the current path (a folder button), cutting the path back when the folder is already
    /// part of it so the path never holds a cycle.
    /// </summary>
    Task OpenFolder(Guid folderId, FolderOpenMode mode);

    /// <summary>Closes the innermost open folder.</summary>
    Task FolderBack();

    /// <summary>Cuts the open-folder path to <paramref name="depth"/> folders; 0 shows the page.</summary>
    Task NavigateFolderDepth(int depth);

    /// <summary>Closes every open folder and shows the page they were opened from.</summary>
    Task CloseFolders();

    /// <summary>
    /// Opens the given folder path by id (companion follow). Stops at the first id that does not
    /// resolve to a child of the previous folder.
    /// </summary>
    Task SetFolderPath(IReadOnlyList<Guid> folderIds);

    /// <summary>Fired with the folder ids of the new path whenever the open-folder path changes.</summary>
    event Action<IReadOnlyList<Guid>> FolderPathChanged;

    // --- Pages of a custom folder (issue #603) -------------------------------

    /// <summary>Shows page <paramref name="pageIndex"/> (0-based) of the open folder.</summary>
    Task GotoFolderPage(int pageIndex);

    Task NextFolderPage();

    Task PreviousFolderPage();

    /// <summary>What the Back tile does: the previous page, or on the first page leaving the folder.</summary>
    Task FolderBackTile();

    /// <summary>
    /// Inserts an empty page after the current page of the open folder and shows it. A key on the
    /// current page's next-page position moves to the new page's first key, so the next-page tile
    /// that now takes its place hides nothing.
    /// </summary>
    Task AddFolderPage();

    /// <summary>Inserts a copy of the open folder's current page right after it and shows the copy.</summary>
    Task DuplicateFolderPage();

    /// <summary>Removes the current page of the open folder; a folder keeps at least one page.</summary>
    Task DeleteFolderPage();
}

public enum FolderOpenMode
{
    Tree,
    Push
}

public class PageManager : IPageManager
{
    private readonly LoupedeckConfig _config;
    private readonly IDeviceService _deviceService;
    private readonly FolderNavigation.IFolderNavigationService _pluginMenus;

    // Folder layouts whose layer handlers were rewired after load. Only the active workspace's
    // layouts are rewired at start-up, so any other layout is rewired the first time it is shown.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TouchButtonPage, object> _preparedLayouts = new();

    public PageManager(LoupedeckConfig config, IDeviceService deviceService,
        FolderNavigation.IFolderNavigationService pluginMenus)
    {
        _config = config;
        _deviceService = deviceService;
        _pluginMenus = pluginMenus;
    }

    public int PreviousTouchPageIndex { get; set; } = -1;

    public int CurrentTouchPageIndex
    {
        get => _config.CurrentTouchPageIndex;
        set => _config.CurrentTouchPageIndex = value;
    }

    public int CurrentRotaryPageIndex
    {
        get => _config.CurrentRotaryPageIndex;
        set => _config.CurrentRotaryPageIndex = value;
    }

    public ObservableCollection<TouchButtonPage> TouchButtonPages => _config.TouchButtonPages;
    public ObservableCollection<RotaryButtonPage> RotaryButtonPages => _config.RotaryButtonPages;
    public RotaryButtonPage CurrentRotaryButtonPage => _config.CurrentRotaryButtonPage;
    public TouchButtonPage CurrentTouchButtonPage => _config.CurrentTouchButtonPage;
    public SimpleButton[] SimpleButtons => _config.SimpleButtons;

    public bool HasIndependentRotarySides => _deviceService.Device?.HasSideStrips ?? false;

    // Number of knobs per side page on a side-strip device (3 on the Razer's 6).
    public int SideRotaryButtonCount => Math.Max(1, _deviceService.RotaryButtonCount / 2);

    public ObservableCollection<RotaryButtonPage> GetRotaryPages(RotarySide side) => side switch
    {
        RotarySide.Left => _config.LeftRotaryButtonPages,
        RotarySide.Right => _config.RightRotaryButtonPages,
        _ => _config.RotaryButtonPages
    };

    public RotaryButtonPage GetCurrentRotaryPage(RotarySide side) => side switch
    {
        RotarySide.Left => _config.CurrentLeftRotaryButtonPage,
        RotarySide.Right => _config.CurrentRightRotaryButtonPage,
        _ => _config.CurrentRotaryButtonPage
    };

    public int GetCurrentRotaryPageIndex(RotarySide side) => side switch
    {
        RotarySide.Left => _config.CurrentLeftRotaryPageIndex,
        RotarySide.Right => _config.CurrentRightRotaryPageIndex,
        _ => _config.CurrentRotaryPageIndex
    };

    public RotaryButtonPage PeekRotaryPage(RotarySide side, int direction)
    {
        var pages = GetRotaryPages(side);
        var n = pages.Count;
        if (n <= 1) return null;
        var idx = GetCurrentRotaryPageIndex(side);
        var target = idx + direction;
        // Without page wrap the first and last page have no neighbour on the outward side, so a
        // swipe there finds nothing to slide in and snaps back.
        if (!_config.PageWrapEnabled && (target < 0 || target >= n)) return null;
        return pages[(((target % n) + n) % n)];
    }

    private void SetCurrentRotaryPageIndex(RotarySide side, int value)
    {
        switch (side)
        {
            case RotarySide.Left: _config.CurrentLeftRotaryPageIndex = value; break;
            case RotarySide.Right: _config.CurrentRightRotaryPageIndex = value; break;
            default: _config.CurrentRotaryPageIndex = value; break;
        }
    }

    // --- Parameterless (legacy / global) paging ------------------------------
    // On a side-strip device the shared list is empty, so page both columns in
    // lockstep — this keeps the default Next/Previous-Rotary-Page side buttons
    // useful while swipes still page each column on its own.

    public void NextRotaryPage()
    {
        if (HasIndependentRotarySides)
        {
            NextRotaryPage(RotarySide.Left);
            NextRotaryPage(RotarySide.Right);
            return;
        }

        NextRotaryPage(RotarySide.Both);
    }

    public void PreviousRotaryPage()
    {
        if (HasIndependentRotarySides)
        {
            PreviousRotaryPage(RotarySide.Left);
            PreviousRotaryPage(RotarySide.Right);
            return;
        }

        PreviousRotaryPage(RotarySide.Both);
    }

    public void ApplyRotaryPage(int pageIndex, bool init = false)
        => ApplyRotaryPage(RotarySide.Both, pageIndex, init);

    // --- Side-aware paging ----------------------------------------------------

    public void NextRotaryPage(RotarySide side)
    {
        var pages = GetRotaryPages(side);
        if (pages.Count == 0) return;
        var current = GetCurrentRotaryPageIndex(side);
        // Wrap (last -> first) unless the user turned page wrap off, which stops on the last page.
        if (!_config.PageWrapEnabled && current + 1 >= pages.Count) return;
        ApplyRotaryPage(side, (current + 1) % pages.Count);
    }

    public void PreviousRotaryPage(RotarySide side)
    {
        var pages = GetRotaryPages(side);
        if (pages.Count == 0) return;
        var current = GetCurrentRotaryPageIndex(side);
        // Wrap (first -> last) unless the user turned page wrap off, which stops on the first page.
        if (!_config.PageWrapEnabled && current - 1 < 0) return;
        ApplyRotaryPage(side, (current - 1 + pages.Count) % pages.Count);
    }

    public void ApplyRotaryPage(RotarySide side, int pageIndex, bool init = false)
    {
        if (GetCurrentRotaryPageIndex(side) == pageIndex && !init) return;

        var pages = GetRotaryPages(side);
        if (pageIndex < 0 || pageIndex >= pages.Count) return;

        var previousIndex = GetCurrentRotaryPageIndex(side);
        SetCurrentRotaryPageIndex(side, pageIndex);

        foreach (var page in pages)
            page.Selected = false;

        var current = GetCurrentRotaryPage(side);
        current?.Selected = true;

        OnRotaryPageChanged?.Invoke(side, previousIndex, pageIndex);

        if (!init && _config.ShowPageNameOverlayEnabled && current != null)
        {
            _deviceService.ShowTemporaryTextButton(0, current.PageName, 2000);
        }
    }

    public async Task NextTouchPage()
    {
        var count = TouchButtonPages.Count;
        if (count == 0) return;
        // Wrap (last -> first) unless the user turned page wrap off, which stops on the last page.
        if (!_config.PageWrapEnabled && CurrentTouchPageIndex + 1 >= count) return;
        await ApplyTouchPage((CurrentTouchPageIndex + 1) % count);
    }

    public async Task PreviousTouchPage()
    {
        var count = TouchButtonPages.Count;
        if (count == 0) return;
        // Wrap (first -> last) unless the user turned page wrap off, which stops on the first page.
        if (!_config.PageWrapEnabled && CurrentTouchPageIndex - 1 < 0) return;
        await ApplyTouchPage((CurrentTouchPageIndex - 1 + count) % count);
    }

    public async Task ApplyTouchPage(int pageIndex, bool init = false, bool draw = true)
    {
        // The same page again keeps any open folder: context rules re-apply their page on every
        // focus change and must not throw the user out of a folder.
        if (CurrentTouchPageIndex == pageIndex) return;

        // Leaving for another page closes the folders opened from the current one.
        ClearFolderPath();

        PreviousTouchPageIndex = CurrentTouchPageIndex;
        CurrentTouchPageIndex = pageIndex;

        foreach (var page in TouchButtonPages)
        {
            page.Selected = false;
        }

        CurrentTouchButtonPage.Selected = true;

        OnTouchPageChanged?.Invoke(PreviousTouchPageIndex, CurrentTouchPageIndex);
        TouchLayoutChanged?.Invoke();

        // The animated path (draw:false) commits page state only; it renders the incoming
        // page and paints the slide/final frame itself, so skip the slot-by-slot redraw and
        // the page-name overlay here (the controller re-shows the overlay after the slide).
        if (!draw) return;

        // A plugin menu open over the page is left with it, like the custom folders above.
        // Painting the new page under an open menu put it over the menu's keys while presses
        // still went to the menu. Closing the menu repaints the page that is current now, so
        // it is not painted here as well.
        if (_pluginMenus.IsActive)
            await _pluginMenus.ExitAll();
        else
            await DrawTouchButtons();

        if (!init && _config.ShowPageNameOverlayEnabled)
        {
            // Fire-and-forget: the 2s on-device overlay must not block callers
            // (e.g. AddTouchButtonPage), which would otherwise leave the
            // triggering UI command disabled for the full duration.
            _ = _deviceService.ShowTemporaryTextButton(0, CurrentTouchButtonPage.PageName, 2000);
        }
    }

    private async Task DrawTouchButtons()
    {
        LoupedeckDevice.Device.LoupedeckDevice device = _deviceService.Device;
        if (device == null) return;

        // One region write for the whole page: a single framebuffer write and DRAW instead of
        // one of each per key, and it also covers the pixels between the keys of a grid with
        // gaps, where the previous page would otherwise stay standing.
        await device.DrawTouchGridRegion(CurrentTouchButtonPage.TouchButtons, _config);

        NotifyTouchLayoutDrawn();
    }

    public void AddRotaryButtonPage(bool init = false)
    {
        // On a side-strip device, the shared list is unused — add a page to each
        // column so the global "add rotary page" control keeps both sides in sync.
        if (HasIndependentRotarySides)
        {
            AddRotaryButtonPage(RotarySide.Left, init);
            AddRotaryButtonPage(RotarySide.Right, init);
            return;
        }

        AddRotaryButtonPage(RotarySide.Both, init);
    }

    public void DeleteRotaryButtonPage()
    {
        if (HasIndependentRotarySides)
        {
            DeleteRotaryButtonPage(RotarySide.Left);
            DeleteRotaryButtonPage(RotarySide.Right);
            return;
        }

        DeleteRotaryButtonPage(RotarySide.Both);
    }

    public void AddRotaryButtonPage(RotarySide side, bool init = false)
    {
        // Side pages hold only that column's knobs; the shared list holds them all.
        var size = side == RotarySide.Both ? _deviceService.RotaryButtonCount : SideRotaryButtonCount;
        var pages = GetRotaryPages(side);

        var newPage = new RotaryButtonPage(size)
        {
            Page = pages.Count + 1,
            Side = side
        };

        pages.Add(newPage);
        ApplyRotaryPage(side, pages.Count - 1, init);
    }

    public void DeleteRotaryButtonPage(RotarySide side)
    {
        var pages = GetRotaryPages(side);
        if (pages.Count <= 1)
            return;

        pages.RemoveAt(GetCurrentRotaryPageIndex(side));
        Renumber(pages);

        var currentIndex = GetCurrentRotaryPageIndex(side);
        if (currentIndex < pages.Count)
            ApplyRotaryPage(side, currentIndex, init: true);
        else
            ApplyRotaryPage(side, pages.Count - 1, init: true);
    }

    public void DuplicateRotaryButtonPage()
    {
        if (HasIndependentRotarySides)
        {
            DuplicateRotaryButtonPage(RotarySide.Left, GetCurrentRotaryPageIndex(RotarySide.Left));
            DuplicateRotaryButtonPage(RotarySide.Right, GetCurrentRotaryPageIndex(RotarySide.Right));
            return;
        }

        DuplicateRotaryButtonPage(RotarySide.Both, GetCurrentRotaryPageIndex(RotarySide.Both));
    }

    public void DuplicateRotaryButtonPage(RotarySide side, int pageIndex)
    {
        var pages = GetRotaryPages(side);
        if (pageIndex < 0 || pageIndex >= pages.Count)
            return;

        RotaryButtonPage copy = PageCloner.Clone(pages[pageIndex], _deviceService.RotaryButtonCount, SideRotaryButtonCount);
        pages.Insert(pageIndex + 1, copy);
        Renumber(pages);

        ApplyRotaryPage(side, pageIndex + 1, init: true);
    }

    public async Task AddTouchButtonPage(bool init = false)
    {
        var touchCount = _deviceService.TouchButtonCount;
        var newPage = new TouchButtonPage(touchCount)
        {
            Page = TouchButtonPages.Count + 1,
            // No wallpaper of its own: with InheritWallpaperFromPreviousPage on, the renderer
            // shows the previous page's, and switching that off leaves the page empty.
            Geometry = _config.Geometry
        };

        for (int i = 0; i < touchCount; i++)
        {
            newPage.TouchButtons[i] = new TouchButton(i);
        }

        TouchButtonPages.Add(newPage);
        await ApplyTouchPage(TouchButtonPages.Count - 1, init);
    }

    public async Task DeleteTouchButtonPage()
    {
        if (TouchButtonPages.Count == 1)
            return;

        TouchButtonPages.RemoveAt(CurrentTouchPageIndex);
        Renumber(TouchButtonPages);

        if (CurrentTouchPageIndex < TouchButtonPages.Count)
        {
            await ApplyTouchPage(CurrentTouchPageIndex);
        }
        else
        {
            await ApplyTouchPage(TouchButtonPages.Count - 1);
        }
    }

    /// <summary>Rewrites each page's 1-based <see cref="ButtonPageBase.Page"/> number from its position.</summary>
    private static void Renumber<T>(IList<T> pages) where T : ButtonPageBase
    {
        for (int i = 0; i < pages.Count; i++)
            pages[i].Page = i + 1;
    }

    public async Task DuplicateTouchButtonPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= TouchButtonPages.Count)
            return;

        TouchButtonPage copy = PageCloner.Clone(TouchButtonPages[pageIndex], _deviceService.TouchButtonCount);
        int insertAt = pageIndex + 1;
        TouchButtonPages.Insert(insertAt, copy);
        Renumber(TouchButtonPages);

        // The startup page is stored by index; keep it on the page it pointed at. Context rules
        // and companion targets reference pages by id and need nothing.
        if (_config.ActiveWorkspace is { } workspace && workspace.StartupTouchPageIndex >= insertAt)
            workspace.StartupTouchPageIndex++;

        await ApplyTouchPage(insertAt);
    }

    public void RefreshTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            touchButton.Refresh();
        }
    }

    public void RefreshSimpleButtons()
    {
        // Null while the active profile's LED buttons have not been built yet (config v12 — each
        // profile owns its own set, and a freshly created one starts without any).
        foreach (var simpleButton in SimpleButtons ?? [])
        {
            simpleButton?.Refresh();
        }
    }

    public event Action<RotarySide, int, int> OnRotaryPageChanged;

    public event Action<int, int> OnTouchPageChanged;

    public event Action TouchLayoutChanged;

    public event Action TouchLayoutDrawn;

    public void NotifyTouchLayoutDrawn() => TouchLayoutDrawn?.Invoke();

    public event Action<IReadOnlyList<Guid>> FolderPathChanged;

    // --- Custom folders (issue #249) -----------------------------------------

    public Task OpenFolder(Guid folderId, FolderOpenMode mode)
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace == null) return Task.CompletedTask;

        List<CustomFolder> treePath = FolderTree.PathTo(workspace, folderId);
        if (treePath == null)
        {
            Console.WriteLine($"OpenFolder: folder {folderId} not found in the active workspace.");
            return Task.CompletedTask;
        }

        List<CustomFolder> path;
        if (mode == FolderOpenMode.Tree)
        {
            path = treePath;
        }
        else
        {
            path = [.. workspace.FolderPath];
            int existing = path.FindIndex(f => f.Id == folderId);
            if (existing >= 0)
                path.RemoveRange(existing + 1, path.Count - existing - 1);
            else
                path.Add(treePath[^1]);
        }

        return ApplyFolderPath(workspace, path);
    }

    public Task FolderBack()
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace is not { IsFolderOpen: true }) return Task.CompletedTask;
        return ApplyFolderPath(workspace, [.. workspace.FolderPath.Take(workspace.FolderPath.Count - 1)]);
    }

    public Task NavigateFolderDepth(int depth)
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace == null || depth < 0 || depth >= workspace.FolderPath.Count) return Task.CompletedTask;
        return ApplyFolderPath(workspace, [.. workspace.FolderPath.Take(depth)]);
    }

    public Task CloseFolders()
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace is not { IsFolderOpen: true }) return Task.CompletedTask;
        return ApplyFolderPath(workspace, []);
    }

    public Task SetFolderPath(IReadOnlyList<Guid> folderIds)
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace == null) return Task.CompletedTask;

        List<CustomFolder> path = [];
        IEnumerable<CustomFolder> level = workspace.Folders ?? [];
        foreach (Guid id in folderIds ?? [])
        {
            CustomFolder next = level.FirstOrDefault(f => f?.Id == id);
            if (next == null) break;
            path.Add(next);
            level = next.Children ?? [];
        }

        return ApplyFolderPath(workspace, path);
    }

    /// <summary>
    /// Closes the open folders of the active workspace without drawing, for callers that repaint
    /// the grid themselves right after (a page switch). Raises the path event only.
    /// </summary>
    private void ClearFolderPath()
    {
        var workspace = _config.ActiveWorkspace;
        if (workspace is not { IsFolderOpen: true }) return;

        workspace.SetFolderPath([]);
        FolderPathChanged?.Invoke([]);
    }

    private async Task ApplyFolderPath(Workspace workspace, IReadOnlyList<CustomFolder> path)
    {
        if (workspace.FolderPath.SequenceEqual(path)) return;

        if (path.Count > 0)
        {
            // A folder opened anew starts on its first page; one returned to from a sub-folder
            // keeps the page it was left on.
            if (!workspace.FolderPath.Contains(path[^1]))
                path[^1].CurrentPageIndex = 0;
            PrepareFolderLayout(path[^1]);
        }

        // The path is committed before a plugin menu is closed: closing it repaints whatever is
        // current, which then already is the folder, instead of racing a second repaint here.
        workspace.SetFolderPath(path);
        TouchLayoutChanged?.Invoke();
        FolderPathChanged?.Invoke([.. path.Select(static f => f.Id)]);

        if (_pluginMenus.IsActive)
        {
            await _pluginMenus.ExitAll();
            return;
        }

        await DrawTouchButtons();

        if (_config.ShowPageNameOverlayEnabled)
        {
            string name = workspace.OpenFolder?.Name ?? CurrentTouchButtonPage?.PageName;
            if (!string.IsNullOrEmpty(name))
                _ = _deviceService.ShowTemporaryTextButton(0, name, 2000);
        }
    }

    public Task NextFolderPage() =>
        _config.ActiveWorkspace?.OpenFolder is { } folder ? GotoFolderPage(folder.CurrentPageIndex + 1) : Task.CompletedTask;

    public Task PreviousFolderPage() =>
        _config.ActiveWorkspace?.OpenFolder is { } folder ? GotoFolderPage(folder.CurrentPageIndex - 1) : Task.CompletedTask;

    public Task FolderBackTile()
    {
        CustomFolder folder = _config.ActiveWorkspace?.OpenFolder;
        if (folder == null) return Task.CompletedTask;
        return folder.CurrentPageIndex > 0 ? GotoFolderPage(folder.CurrentPageIndex - 1) : FolderBack();
    }

    public Task GotoFolderPage(int pageIndex)
    {
        Workspace workspace = _config.ActiveWorkspace;
        CustomFolder folder = workspace?.OpenFolder;
        if (folder == null || pageIndex < 0 || pageIndex >= folder.PageCount || pageIndex == folder.CurrentPageIndex)
            return Task.CompletedTask;

        folder.CurrentPageIndex = pageIndex;
        return ShowFolderPage(workspace, folder);
    }

    public Task AddFolderPage()
    {
        Workspace workspace = _config.ActiveWorkspace;
        CustomFolder folder = workspace?.OpenFolder;
        if (folder == null) return Task.CompletedTask;

        PrepareFolderLayout(folder);
        TouchButtonPage current = folder.CurrentLayout;
        TouchButtonPage page = NewFolderPage();

        // The current page gets a next-page tile on its bottom-right key; what was there moves to
        // the new page's first key.
        TouchButton overflow = current.TouchButtons.FindByIndex(_pluginMenus.Grid.NextSlotIndex);
        TouchButton first = page.TouchButtons.FindByIndex(0);
        if (overflow != null && first != null && !overflow.IsFolderNavTile && !ButtonSnapshot.IsEmpty(overflow))
            SwapKeys(current, overflow, page, first);

        List<TouchButtonPage> pages = [.. folder.Pages()];
        pages.Insert(folder.CurrentPageIndex + 1, page);
        SetFolderPages(folder, pages);

        folder.CurrentPageIndex++;
        return ShowFolderPage(workspace, folder);
    }

    public Task DuplicateFolderPage()
    {
        Workspace workspace = _config.ActiveWorkspace;
        CustomFolder folder = workspace?.OpenFolder;
        if (folder == null) return Task.CompletedTask;

        PrepareFolderLayout(folder);
        TouchButtonPage current = folder.CurrentLayout;
        TouchButtonPage copy = PageCloner.Clone(current, _deviceService.TouchButtonCount);

        // The copy follows the current page, which then gets a next-page tile. A key on that
        // position stays on the copy only, instead of hiding under the tile.
        int nextIndex = _pluginMenus.Grid.NextSlotIndex;
        TouchButton overflow = current.TouchButtons.FindByIndex(nextIndex);
        if (overflow != null && !overflow.IsFolderNavTile && !ButtonSnapshot.IsEmpty(overflow))
            current.TouchButtons[current.TouchButtons.IndexOf(overflow)] = new TouchButton(nextIndex);

        List<TouchButtonPage> pages = [.. folder.Pages()];
        pages.Insert(folder.CurrentPageIndex + 1, copy);
        SetFolderPages(folder, pages);

        folder.CurrentPageIndex++;
        return ShowFolderPage(workspace, folder);
    }

    public Task DeleteFolderPage()
    {
        Workspace workspace = _config.ActiveWorkspace;
        CustomFolder folder = workspace?.OpenFolder;
        if (folder == null || folder.PageCount <= 1) return Task.CompletedTask;

        List<TouchButtonPage> pages = [.. folder.Pages()];
        pages.RemoveAt(folder.CurrentPageIndex);
        SetFolderPages(folder, pages);

        folder.CurrentPageIndex = Math.Min(folder.CurrentPageIndex, pages.Count - 1);
        return ShowFolderPage(workspace, folder);
    }

    /// <summary>Repaints the open folder after its current page or its set of pages changed.</summary>
    private async Task ShowFolderPage(Workspace workspace, CustomFolder folder)
    {
        PrepareFolderLayout(folder);
        workspace.NotifyFolderPageChanged();
        TouchLayoutChanged?.Invoke();

        if (_pluginMenus.IsActive)
        {
            await _pluginMenus.ExitAll();
            return;
        }

        await DrawTouchButtons();

        if (_config.ShowPageNameOverlayEnabled && folder.CurrentLayout is { } layout)
            _ = _deviceService.ShowTemporaryTextButton(0, layout.PageName, 2000);
    }

    /// <summary>An empty folder page for this device.</summary>
    private TouchButtonPage NewFolderPage()
    {
        // Like a new page, a new folder page has no wallpaper of its own; with
        // InheritWallpaperFromPreviousPage on it shows the one of the page the folder is opened from.
        int keyCount = _deviceService.TouchButtonCount;
        TouchButtonPage page = new(keyCount) { Geometry = _config.Geometry };
        for (int i = 0; i < keyCount; i++)
            page.TouchButtons[i] = new TouchButton(i);
        _preparedLayouts.AddOrUpdate(page, null);
        return page;
    }

    /// <summary>Stores <paramref name="pages"/> as the folder's pages, first page first, and numbers them.</summary>
    private static void SetFolderPages(CustomFolder folder, List<TouchButtonPage> pages)
    {
        for (int i = 0; i < pages.Count; i++)
            pages[i].Page = i + 1;

        // A new list rather than an edit of the old one: the device read thread may be reading it.
        folder.ExtraPages = pages.GetRange(1, pages.Count - 1);
        folder.Layout = pages[0];
    }

    /// <summary>Exchanges two keys, on one layout or across two, so each position keeps its index.</summary>
    private static void SwapKeys(TouchButtonPage layoutA, TouchButton a, TouchButtonPage layoutB, TouchButton b)
    {
        int positionA = layoutA.TouchButtons.IndexOf(a);
        int positionB = layoutB.TouchButtons.IndexOf(b);
        (a.Index, b.Index) = (b.Index, a.Index);
        layoutA.TouchButtons[positionA] = b;
        layoutB.TouchButtons[positionB] = a;
    }

    /// <summary>
    /// Makes the current page of a folder ready to show: creates the folder's layout on first open,
    /// pads the page to the device's key count, rewires its layers once and marks the navigation
    /// tiles: Back on every page, Next on every page but the last (issue #603). A page made on a
    /// device with a different grid can hold content on one of those keys; that content moves to the
    /// first free key so nothing is hidden behind a tile.
    /// </summary>
    private void PrepareFolderLayout(CustomFolder folder)
    {
        int keyCount = _deviceService.TouchButtonCount;

        folder.Layout ??= NewFolderPage();
        folder.CurrentPageIndex = Math.Clamp(folder.CurrentPageIndex, 0, folder.PageCount - 1);

        int number = 1;
        foreach (TouchButtonPage page in folder.Pages())
            page.Page = number++;

        TouchButtonPage layout = folder.CurrentLayout;
        layout.Geometry = _config.Geometry;

        for (int i = layout.TouchButtons.Count; i < keyCount; i++)
            layout.TouchButtons.Add(new TouchButton(i));

        if (!_preparedLayouts.TryGetValue(layout, out _))
        {
            foreach (TouchButton button in layout.TouchButtons)
                button?.RewireLayerHandlers();
            foreach (RotaryButton mode in layout.WheelModes)
                mode?.RewireAfterLoad();
            _preparedLayouts.AddOrUpdate(layout, null);
        }

        FolderNavigation.FolderGrid grid = _pluginMenus.Grid;
        int backIndex = grid.BackSlotIndex;
        int nextIndex = folder.CurrentPageIndex < folder.PageCount - 1 ? grid.NextSlotIndex : -1;

        ClearKeyForTile(folder, layout, grid, backIndex, nextIndex, backIndex);
        if (nextIndex >= 0)
            ClearKeyForTile(folder, layout, grid, backIndex, nextIndex, nextIndex);

        foreach (TouchButton button in layout.TouchButtons)
        {
            button?.FolderNavTile = button.Index == backIndex ? FolderNavTile.Back
                : button.Index == nextIndex ? FolderNavTile.Next
                : FolderNavTile.None;
        }
    }

    /// <summary>
    /// Moves the content of key <paramref name="tileIndex"/>, which a navigation tile is about to
    /// cover, to the first free key that no tile covers.
    /// </summary>
    private static void ClearKeyForTile(CustomFolder folder, TouchButtonPage layout, FolderNavigation.FolderGrid grid,
        int backIndex, int nextIndex, int tileIndex)
    {
        TouchButton covered = layout.TouchButtons.FindByIndex(tileIndex);
        if (covered == null || covered.IsFolderNavTile || ButtonSnapshot.IsEmpty(covered)) return;

        TouchButton free = layout.TouchButtons.FirstOrDefault(b =>
            b != null && b.Index != backIndex && b.Index != nextIndex && grid.IsGridSlot(b.Index) && ButtonSnapshot.IsEmpty(b));
        if (free != null)
        {
            SwapKeys(layout, covered, layout, free);
            Console.WriteLine($"Folder '{folder.Name}': moved the content of key {tileIndex} to key {covered.Index} to make room for a navigation tile.");
        }
        else
        {
            Console.WriteLine($"Folder '{folder.Name}': key {tileIndex} is covered by a navigation tile and no free key was left to move its content to.");
        }
    }

}