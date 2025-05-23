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

    void NextRotaryPage();
    void PreviousRotaryPage();
    void ApplyRotaryPage(int pageIndex, bool init = false);
    Task NextTouchPage();
    Task PreviousTouchPage();
    Task ApplyTouchPage(int pageIndex, bool init = false);

    void AddRotaryButtonPage(bool init = false);
    void DeleteRotaryButtonPage();
    Task AddTouchButtonPage(bool init = false);
    Task DeleteTouchButtonPage();

    void RefreshTouchButtons();
    void RefreshSimpleButtons();
    event Action<int, int> OnRotaryPageChanged;
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

    public void NextRotaryPage()
    {
        ApplyRotaryPage((CurrentRotaryPageIndex + 1) % RotaryButtonPages.Count);
    }

    public void PreviousRotaryPage()
    {
        ApplyRotaryPage((CurrentRotaryPageIndex - 1 + RotaryButtonPages.Count) % RotaryButtonPages.Count);
    }

    public void ApplyRotaryPage(int pageIndex, bool init = false)
    {
        if (CurrentRotaryPageIndex == pageIndex) return;

        var previousRotaryPageIndex = CurrentRotaryPageIndex;
        CurrentRotaryPageIndex = pageIndex;

        foreach (var page in RotaryButtonPages)
        {
            page.Selected = false;
        }

        CurrentRotaryButtonPage.Selected = true;

        OnRotaryPageChanged?.Invoke(previousRotaryPageIndex, CurrentRotaryPageIndex);

        if (!init)
        {
            _deviceService.ShowTemporaryTextButton(0, CurrentRotaryButtonPage.PageName, 2000);
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

        if (!init)
        {
            await _deviceService.ShowTemporaryTextButton(0, CurrentTouchButtonPage.PageName, 2000);
        }
    }

    private async Task DrawTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            await _deviceService.Device.DrawTouchButton(touchButton, _config, false, 5);
        }
    }

    public void AddRotaryButtonPage(bool init = false)
    {
        var newPage = new RotaryButtonPage(2)
        {
            Page = RotaryButtonPages.Count + 1
        };

        RotaryButtonPages.Add(newPage);
        ApplyRotaryPage(RotaryButtonPages.Count - 1, init);
    }

    public void DeleteRotaryButtonPage()
    {
        if (RotaryButtonPages.Count == 1)
            return;

        RotaryButtonPages.RemoveAt(CurrentRotaryPageIndex);

        int counter = 0;
        foreach (var page in RotaryButtonPages)
        {
            counter++;
            page.Page = counter;
        }

        if (CurrentRotaryPageIndex < RotaryButtonPages.Count)
        {
            ApplyRotaryPage(CurrentRotaryPageIndex);
        }
        else
        {
            ApplyRotaryPage(RotaryButtonPages.Count - 1);
        }
    }

    public async Task AddTouchButtonPage(bool init = false)
    {
        var newPage = new TouchButtonPage(15)
        {
            Page = TouchButtonPages.Count + 1
        };

        for (int i = 0; i < 15; i++)
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

    public event Action<int, int> OnRotaryPageChanged;

    public event Action<int, int> OnTouchPageChanged;
}