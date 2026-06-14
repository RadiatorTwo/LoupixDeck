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

public class PageManager : IPageManager
{
    private readonly LoupedeckConfig _config;
    private readonly IDeviceService _deviceService;

    public PageManager(LoupedeckConfig config, IDeviceService deviceService)
    {
        _config = config;
        _deviceService = deviceService;
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
    private int SideRotaryButtonCount => Math.Max(1, _deviceService.RotaryButtonCount / 2);

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
        var target = (((idx + direction) % n) + n) % n;
        return pages[target];
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
        if (current != null)
            current.Selected = true;

        OnRotaryPageChanged?.Invoke(side, previousIndex, pageIndex);

        if (!init && _config.ShowPageNameOverlayEnabled && current != null)
        {
            _deviceService.ShowTemporaryTextButton(0, current.PageName, 2000);
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
        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            // Force refresh to ensure wallpaper changes are applied when switching pages
            await _deviceService.Device.DrawTouchButton(touchButton, _config, true, _deviceService.Device.Columns);
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
            Wallpaper = previous?.Wallpaper,
            WallpaperOpacity = previous?.WallpaperOpacity ?? 0
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