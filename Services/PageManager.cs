using System.Collections.ObjectModel;
using System.Security;
using LoupixDeck.Models;
using Avalonia.Threading;
using AutoMapper;

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
    void ApplyRotaryPage(int pageIndex);
    void NextTouchPage();
    void PreviousTouchPage();
    void ApplyTouchPage(int pageIndex);

    void AddRotaryButtonPage();
    void DeleteRotaryButtonPage();
    void AddTouchButtonPage();
    void DeleteTouchButtonPage();

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

    public int PreviousRotaryPageIndex { get; set; } = -1;
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

    public void ApplyRotaryPage(int pageIndex)
    {
        if (CurrentRotaryPageIndex == pageIndex) return;

        PreviousRotaryPageIndex = CurrentRotaryPageIndex;
        CurrentRotaryPageIndex = pageIndex;

        OnRotaryPageChanged?.Invoke(PreviousRotaryPageIndex, CurrentRotaryPageIndex);
    }

    public void NextTouchPage()
    {
        ApplyTouchPage((CurrentTouchPageIndex + 1) % TouchButtonPages.Count);
    }

    public void PreviousTouchPage()
    {
        ApplyTouchPage((CurrentTouchPageIndex - 1 + TouchButtonPages.Count) % TouchButtonPages.Count);
    }

    public void ApplyTouchPage(int pageIndex)
    {
        PreviousTouchPageIndex = CurrentTouchPageIndex;
        CurrentTouchPageIndex = pageIndex;

        OnTouchPageChanged?.Invoke(PreviousTouchPageIndex, CurrentTouchPageIndex);
        DrawTouchButtons();
    }

    private void DrawTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage.TouchButtons)
        {
            _deviceService.Device.DrawTouchButton(touchButton, false);
        }
    }

    public void AddRotaryButtonPage()
    {
        var newPage = new RotaryButtonPage(2)
        {
            Page = RotaryButtonPages.Count + 1
        };
        RotaryButtonPages.Add(newPage);
        CurrentRotaryPageIndex = RotaryButtonPages.Count - 1;

        ApplyRotaryPage(CurrentRotaryPageIndex);
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

    public void AddTouchButtonPage()
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
        ApplyTouchPage(CurrentTouchPageIndex + 1);
    }

    public void DeleteTouchButtonPage()
    {
        if (TouchButtonPages.Count == 1)
            return;

        TouchButtonPages.RemoveAt(CurrentTouchPageIndex);

        int counter = 0;
        foreach (var page in TouchButtonPages)
        {
            counter++;
            page.Page = counter;
        }

        if (CurrentTouchPageIndex < TouchButtonPages.Count)
        {
            ApplyTouchPage(CurrentTouchPageIndex);
        }
        else
        {
            ApplyTouchPage(TouchButtonPages.Count - 1);
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