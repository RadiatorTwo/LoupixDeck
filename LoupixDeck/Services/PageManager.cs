using System.Collections.ObjectModel;
using LoupixDeck.Models;

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
    Task ApplyTouchPage(int pageIndex, bool init = false);

    void AddRotaryButtonPage(bool init = false);
    void DeleteRotaryButtonPage();
    Task AddTouchButtonPage(bool init = false);
    Task DeleteTouchButtonPage();

    void RefreshTouchButtons();
    void RefreshSimpleButtons();

    /// <summary>Fired when a rotary page changes: (side, previousIndex, newIndex).</summary>
    event Action<RotarySide, int, int> OnRotaryPageChanged;
    event Action<int, int> OnTouchPageChanged;
}

public class PageManager(LoupedeckConfig config, IDeviceService deviceService) : IPageManager
{
    public int PreviousTouchPageIndex { get; set; } = -1;

    public int CurrentTouchPageIndex
    {
        get => config.CurrentTouchPageIndex;
        set => config.CurrentTouchPageIndex = value;
    }

    public int CurrentRotaryPageIndex
    {
        get => config.CurrentRotaryPageIndex;
        set => config.CurrentRotaryPageIndex = value;
    }

    public ObservableCollection<TouchButtonPage> TouchButtonPages => config.TouchButtonPages;
    public ObservableCollection<RotaryButtonPage> RotaryButtonPages => config.RotaryButtonPages;
    public RotaryButtonPage CurrentRotaryButtonPage => config.CurrentRotaryButtonPage;
    public TouchButtonPage CurrentTouchButtonPage => config.CurrentTouchButtonPage;
    public SimpleButton[] SimpleButtons => config.SimpleButtons;

    public bool HasIndependentRotarySides => deviceService.Device?.HasSideStrips ?? false;

    // Number of knobs per side page on a side-strip device (3 on the Razer's 6).
    private int SideRotaryButtonCount => Math.Max(1, deviceService.RotaryButtonCount / 2);

    public ObservableCollection<RotaryButtonPage> GetRotaryPages(RotarySide side) => side switch
    {
        RotarySide.Left => config.LeftRotaryButtonPages,
        RotarySide.Right => config.RightRotaryButtonPages,
        _ => config.RotaryButtonPages
    };

    public RotaryButtonPage GetCurrentRotaryPage(RotarySide side) => side switch
    {
        RotarySide.Left => config.CurrentLeftRotaryButtonPage,
        RotarySide.Right => config.CurrentRightRotaryButtonPage,
        _ => config.CurrentRotaryButtonPage
    };

    public int GetCurrentRotaryPageIndex(RotarySide side) => side switch
    {
        RotarySide.Left => config.CurrentLeftRotaryPageIndex,
        RotarySide.Right => config.CurrentRightRotaryPageIndex,
        _ => config.CurrentRotaryPageIndex
    };

    public RotaryButtonPage PeekRotaryPage(RotarySide side, int direction)
    {
        var pages = GetRotaryPages(side);
        var n = pages.Count;
        if (n <= 1) return null;
        var idx = GetCurrentRotaryPageIndex(side);
        var target = (((idx + direction) % n) + n) % n;
        return pages[target];
    }

    private void SetCurrentRotaryPageIndex(RotarySide side, int value)
    {
        switch (side)
        {
            case RotarySide.Left: config.CurrentLeftRotaryPageIndex = value; break;
            case RotarySide.Right: config.CurrentRightRotaryPageIndex = value; break;
            default: config.CurrentRotaryPageIndex = value; break;
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
        ApplyRotaryPage(side, (GetCurrentRotaryPageIndex(side) + 1) % pages.Count);
    }

    public void PreviousRotaryPage(RotarySide side)
    {
        var pages = GetRotaryPages(side);
        if (pages.Count == 0) return;
        ApplyRotaryPage(side, (GetCurrentRotaryPageIndex(side) - 1 + pages.Count) % pages.Count);
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

        if (!init && config.ShowPageNameOverlayEnabled && current != null)
        {
            deviceService.ShowTemporaryTextButton(0, current.PageName, 2000);
        }
    }

    public async Task NextTouchPage()
    {
        await ApplyTouchPage((CurrentTouchPageIndex + 1) % TouchButtonPages.Count);
    }

    public async Task PreviousTouchPage()
    {
        await ApplyTouchPage((CurrentTouchPageIndex - 1 + TouchButtonPages.Count) % TouchButtonPages.Count);
    }

    public async Task ApplyTouchPage(int pageIndex, bool init = false)
    {
        if (CurrentTouchPageIndex == pageIndex) return;

        PreviousTouchPageIndex = CurrentTouchPageIndex;
        CurrentTouchPageIndex = pageIndex;

        foreach (var page in TouchButtonPages)
        {
            page.Selected = false;
        }

        CurrentTouchButtonPage.Selected = true;

        OnTouchPageChanged?.Invoke(PreviousTouchPageIndex, CurrentTouchPageIndex);
        await DrawTouchButtons();

        if (!init && config.ShowPageNameOverlayEnabled)
        {
            // Fire-and-forget: the 2s on-device overlay must not block callers
            // (e.g. AddTouchButtonPage), which would otherwise leave the
            // triggering UI command disabled for the full duration.
            _ = deviceService.ShowTemporaryTextButton(0, CurrentTouchButtonPage.PageName, 2000);
        }
    }

    private async Task DrawTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            // Force refresh to ensure wallpaper changes are applied when switching pages
            await deviceService.Device.DrawTouchButton(touchButton, config, true, deviceService.Device.Columns);
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
        var size = side == RotarySide.Both ? deviceService.RotaryButtonCount : SideRotaryButtonCount;
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

        var touchCount = deviceService.TouchButtonCount;
        var newPage = new TouchButtonPage(touchCount)
        {
            Page = TouchButtonPages.Count + 1,
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
        foreach (var simpleButton in SimpleButtons)
        {
            simpleButton.Refresh();
        }
    }

    public event Action<RotarySide, int, int> OnRotaryPageChanged;

    public event Action<int, int> OnTouchPageChanged;
}