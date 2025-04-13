using System.Collections.ObjectModel;
using System.Security;
using LoupixDeck.Models;
using Avalonia.Threading;
using AutoMapper;

namespace LoupixDeck.Services;

public interface IPageManager
{
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

    void CopyRotaryButtonData(RotaryButton source);
    void CopyBackRotaryButtonData(TouchButton source);

    void CopyCurrentTouchButtonsToPage();
    void CopyTouchButtonData(TouchButton source);
    void CopyBackTouchButtonData(TouchButton source);

    void RefreshTouchButtons();
    void RefreshSimpleButtons();
}

public class PageManager : IPageManager
{
    private readonly IMapper _mapper;
    private readonly LoupedeckConfig _config;

    public PageManager(IMapper mapper, LoupedeckConfig config)
    {
        _mapper = mapper;
        _config = config;
    }

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
    public RotaryButtonPage CurrentRotaryButtonPage
    {
        get => _config.CurrentRotaryButtonPage;
        set => _config.CurrentRotaryButtonPage = value;
    }

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
        // Touch-Buttons der neuen Seite in das aktuelle Array kopieren.
        foreach (var rotaryButton in RotaryButtonPages[pageIndex].RotaryButtons)
        {
            CopyRotaryButtonData(rotaryButton);
        }
        
        CurrentRotaryPageIndex = pageIndex;
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
        // Touch-Buttons der neuen Seite in das aktuelle Array kopieren.
        foreach (var touchButton in TouchButtonPages[pageIndex].TouchButtons)
        {
            CopyTouchButtonData(touchButton);
        }

        CurrentTouchPageIndex = pageIndex;
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
        ApplyTouchPage(CurrentTouchPageIndex);
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

    public void CopyRotaryButtonData(RotaryButton source)
    {
        CurrentRotaryButtonPage ??= new RotaryButtonPage(2);
        
        if (CurrentRotaryButtonPage.RotaryButtons[source.Index] == null)
        {
            CurrentRotaryButtonPage.RotaryButtons[source.Index] =
                new RotaryButton(source.Index, string.Empty, string.Empty);
        }

        CurrentRotaryButtonPage.RotaryButtons[source.Index].IgnoreRefresh = true;
        _mapper.Map(source, CurrentRotaryButtonPage.RotaryButtons[source.Index]);
        CurrentRotaryButtonPage.RotaryButtons[source.Index].IgnoreRefresh = false;

        Dispatcher.UIThread.Post(() => { CurrentRotaryButtonPage.RotaryButtons[source.Index].Refresh(); });
    }

    public void CopyBackRotaryButtonData(TouchButton source)
    {
        if (RotaryButtonPages[CurrentRotaryPageIndex] == null)
            return;

        if (RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[source.Index] == null)
        {
            RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[source.Index] =
                new RotaryButton(source.Index, string.Empty, string.Empty);
        }

        _mapper.Map(source, RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[source.Index]);
    }

    public void CopyCurrentTouchButtonsToPage()
    {
        foreach (var currentTouchButton in CurrentTouchButtonPage.TouchButtons)
        {
            CopyBackTouchButtonData(currentTouchButton);
        }
    }

    public void CopyTouchButtonData(TouchButton source)
    {
        if (CurrentTouchButtonPage.TouchButtons[source.Index] == null)
        {
            CurrentTouchButtonPage.TouchButtons[source.Index] = new TouchButton(source.Index);
        }

        CurrentTouchButtonPage.TouchButtons[source.Index].IgnoreRefresh = true;
        _mapper.Map(source, CurrentTouchButtonPage.TouchButtons[source.Index]);
        CurrentTouchButtonPage.TouchButtons[source.Index].IgnoreRefresh = false;

        Dispatcher.UIThread.Post(() => { CurrentTouchButtonPage.TouchButtons[source.Index].Refresh(); });
    }

    public void CopyBackTouchButtonData(TouchButton source)
    {
        if (TouchButtonPages[CurrentTouchPageIndex] == null)
            return;

        if (TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index] == null)
        {
            TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index] = new TouchButton(source.Index);
        }

        _mapper.Map(source, TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index]);
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
}