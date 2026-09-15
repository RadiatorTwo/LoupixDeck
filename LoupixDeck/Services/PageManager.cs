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

        // A grid with gaps has to go out as one region: a per-key write never touches the
        // pixels between the keys, so the previous page would stay standing there.
        if (device.KeyGridHasGaps)
        {
            await device.DrawTouchGridRegion(CurrentTouchButtonPage.TouchButtons, _config);
            return;
        }

        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            // Force refresh to ensure wallpaper changes are applied when switching pages
            await device.DrawTouchButton(touchButton, _config, true);
        }
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

        var counter = 0;
        foreach (var page in pages)
        {
            counter++;
            page.Page = counter;
        }

        var currentIndex = GetCurrentRotaryPageIndex(side);
        if (currentIndex < pages.Count)
            ApplyRotaryPage(side, currentIndex, init: true);
        else
            ApplyRotaryPage(side, pages.Count - 1, init: true);
    }

    public async Task AddTouchButtonPage(bool init = false)
    {
        var previous = TouchButtonPages.Count > 0
            ? TouchButtonPages[TouchButtonPages.Count - 1]
            : null;

        var touchCount = _deviceService.TouchButtonCount;
        var newPage = new TouchButtonPage(touchCount)
        {
            Page = TouchButtonPages.Count + 1,
            Geometry = _config.Geometry,
            // Carry over the wallpapers by cloning their persistent parameters
            // (the baked bitmaps are just render caches).
            MainWallpaper = previous?.MainWallpaper?.Clone() ?? new WallpaperSlot(),
            LeftWallpaper = previous?.LeftWallpaper?.Clone() ?? new WallpaperSlot(),
            RightWallpaper = previous?.RightWallpaper?.Clone() ?? new WallpaperSlot()
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

        var counter = 0;
        foreach (var page in TouchButtonPages)
        {
            counter++;
            page.Page = counter;
        }

        if (CurrentTouchPageIndex < TouchButtonPages.Count)
        {
            await ApplyTouchPage(CurrentTouchPageIndex);
        }
        else
        {
            await ApplyTouchPage(TouchButtonPages.Count - 1);
        }
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
            PrepareFolderLayout(path[^1]);

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

    /// <summary>
    /// Makes a folder's layout ready to show: creates it on first open, pads it to the device's key
    /// count, rewires its layers once and marks the Back tile. A layout made on a device with a
    /// different grid can hold content on this device's Back key; that content moves to the first
    /// free key so nothing is hidden behind the tile.
    /// </summary>
    private void PrepareFolderLayout(CustomFolder folder)
    {
        int keyCount = _deviceService.TouchButtonCount;

        if (folder.Layout == null)
        {
            var source = _config.ActiveWorkspace?.CurrentPageLayout ?? TouchButtonPages?.FirstOrDefault();
            folder.Layout = new TouchButtonPage(keyCount)
            {
                // A new folder starts on the wallpaper of the page it is opened from, like a new page.
                MainWallpaper = source?.MainWallpaper?.Clone() ?? new WallpaperSlot(),
                LeftWallpaper = source?.LeftWallpaper?.Clone() ?? new WallpaperSlot(),
                RightWallpaper = source?.RightWallpaper?.Clone() ?? new WallpaperSlot()
            };
            _preparedLayouts.AddOrUpdate(folder.Layout, null);
        }

        TouchButtonPage layout = folder.Layout;
        layout.Geometry = _config.Geometry;

        for (int i = layout.TouchButtons.Count; i < keyCount; i++)
            layout.TouchButtons.Add(new TouchButton(i));

        if (!_preparedLayouts.TryGetValue(layout, out _))
        {
            foreach (TouchButton button in layout.TouchButtons)
                button?.RewireLayerHandlers();
            _preparedLayouts.AddOrUpdate(layout, null);
        }

        FolderNavigation.FolderGrid grid = _pluginMenus.Grid;
        int backIndex = grid.BackSlotIndex;

        TouchButton back = layout.TouchButtons.FindByIndex(backIndex);
        if (back != null && !back.IsFolderBackSlot && !ButtonSnapshot.IsEmpty(back))
        {
            TouchButton free = layout.TouchButtons.FirstOrDefault(b =>
                b != null && b.Index != backIndex && grid.IsGridSlot(b.Index) && ButtonSnapshot.IsEmpty(b));
            if (free != null)
            {
                int backPosition = layout.TouchButtons.IndexOf(back);
                int freePosition = layout.TouchButtons.IndexOf(free);
                (back.Index, free.Index) = (free.Index, back.Index);
                layout.TouchButtons[backPosition] = free;
                layout.TouchButtons[freePosition] = back;
                Console.WriteLine($"Folder '{folder.Name}': moved the content of key {backIndex} to key {back.Index} to make room for the Back tile.");
            }
            else
            {
                Console.WriteLine($"Folder '{folder.Name}': key {backIndex} is covered by the Back tile and no free key was left to move its content to.");
            }
        }

        foreach (TouchButton button in layout.TouchButtons)
            button?.IsFolderBackSlot = button.Index == backIndex;
    }
}