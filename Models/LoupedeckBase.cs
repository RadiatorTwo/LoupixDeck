using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoupixDeck.LoupedeckDevice.Device;

namespace LoupixDeck.Models;

public abstract class LoupedeckBase : INotifyPropertyChanged
{
    private int _currentTouchPageIndex;

    [JsonIgnore]
    protected int CurrentTouchPageIndex
    {
        get => _currentTouchPageIndex;
        set
        {
            _currentTouchPageIndex = value;

            foreach (var page in TouchButtonPages)
            {
                page.IsSelected = page.Page == _currentTouchPageIndex + 1;
            }

            OnPropertyChanged();
        }
    }

    private int _currentRotaryPageIndex;

    [JsonIgnore]
    protected int CurrentRotaryPageIndex
    {
        get => _currentRotaryPageIndex;
        set
        {
            _currentRotaryPageIndex = value;

            foreach (var page in RotaryButtonPages)
            {
                page.IsSelected = page.Page == _currentRotaryPageIndex + 1;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentRotaryButtonsPage));
        }
    }

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonsPage
    {
        get
        {
            if (CurrentRotaryPageIndex >= 0 && CurrentRotaryPageIndex < RotaryButtonPages.Count)
            {
                return RotaryButtonPages[CurrentRotaryPageIndex];
            }

            return null;
        }
        set
        {
            if (CurrentRotaryPageIndex < 0 || CurrentRotaryPageIndex >= RotaryButtonPages.Count) return;

            RotaryButtonPages[CurrentRotaryPageIndex] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotaryButtonPages));
        }
    }

    protected LoupedeckLiveSDevice Device;
    protected readonly CommandRunner CommandRunner;
    protected readonly ObsController Obs;
    protected readonly DBusController DBus;


    public ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; }
    public ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; }
    public TouchButton[] CurrentTouchButtonPage { get; set; }
    public SimpleButton[] SimpleButtons { get; set; }

    private double _brightness = 1;

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (value.Equals(_brightness)) return;

            _brightness = value;
            Device.SetBrightness(_brightness);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected readonly AutoResetEvent DeviceCreatedEvent = new(false);

    protected LoupedeckBase()
    {
        CommandRunner = new CommandRunner();
        Obs = new ObsController();
        Obs.Connect();
        DBus =  new DBusController();
    }

    public void SaveToFile()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new ColorJsonConverter(), new BitmapJsonConverter() }
        };

        var json = JsonSerializer.Serialize(this, options);
        var filePath = FileDialogHelper.GetConfigPath("config.json");
        File.WriteAllText(filePath, json);
    }

    public static T LoadFromFile<T>() where T : LoupedeckBase
    {
        var filePath = FileDialogHelper.GetConfigPath("config.json");

        if (!File.Exists(filePath))
            return null;

        JsonSerializerOptions options = new()
        {
            Converters = { new ColorJsonConverter(), new BitmapJsonConverter() }
        };

        var json = File.ReadAllText(filePath);

        var instance = JsonSerializer.Deserialize<T>(json, options);
        instance.CurrentTouchPageIndex = 0;
        instance.CurrentRotaryPageIndex = 0;

        instance.InitUpdateEvents();

        return instance;
    }

    protected void InitUpdateEvents()
    {
        foreach (var touchButton in CurrentTouchButtonPage)
        {
            touchButton.ItemChanged += TouchItemChanged;
        }

        foreach (var simpleButton in SimpleButtons)
        {
            simpleButton.ItemChanged += SimpleButtonChanged;
        }
    }

    protected void NextRotaryPage()
    {
        CurrentRotaryPageIndex = (CurrentRotaryPageIndex + 1) % RotaryButtonPages.Count;
    }

    protected void PreviousRotaryPage()
    {
        CurrentRotaryPageIndex = (CurrentRotaryPageIndex - 1 + RotaryButtonPages.Count) % RotaryButtonPages.Count;
    }

    public void ApplyRotaryPage(int pageIndex)
    {
        CurrentRotaryPageIndex = pageIndex;
    }

    protected void NextTouchPage()
    {
        ApplyTouchPage((CurrentTouchPageIndex + 1) % TouchButtonPages.Count);
    }

    protected void PreviousTouchPage()
    {
        ApplyTouchPage((CurrentTouchPageIndex - 1 + TouchButtonPages.Count) % TouchButtonPages.Count);
    }

    public void ApplyTouchPage(int pageIndex)
    {
        // Copy the TouchButtons of the new page to `CurrentTouchButtons`.
        foreach (var touchButton in TouchButtonPages[pageIndex].TouchButtons)
        {
            CopyTouchButtonData(touchButton);
        }

        CurrentTouchPageIndex = pageIndex;
    }

    public void RefreshTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage)
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

    private void CopyTouchButtonData(TouchButton source)
    {
        if (CurrentTouchButtonPage[source.Index] == null)
        {
            CurrentTouchButtonPage[source.Index] = new TouchButton(source.Index);
        }

        CurrentTouchButtonPage[source.Index].IgnoreRefresh = true;

        CurrentTouchButtonPage[source.Index].Text = source.Text;
        CurrentTouchButtonPage[source.Index].TextColor = source.TextColor;
        CurrentTouchButtonPage[source.Index].TextCentered = source.TextCentered;
        CurrentTouchButtonPage[source.Index].TextPositionX = source.TextPositionX;
        CurrentTouchButtonPage[source.Index].TextPositionY = source.TextPositionY;
        CurrentTouchButtonPage[source.Index].TextSize = source.TextSize;
        CurrentTouchButtonPage[source.Index].Image = source.Image;
        CurrentTouchButtonPage[source.Index].BackColor = source.BackColor;
        CurrentTouchButtonPage[source.Index].Command = source.Command;
        CurrentTouchButtonPage[source.Index].RenderedImage = source.RenderedImage;

        CurrentTouchButtonPage[source.Index].IgnoreRefresh = false;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => { CurrentTouchButtonPage[source.Index].Refresh(); });
    }

    protected void CopyBackTouchButtonData(TouchButton source)
    {
        // Check if Page exists.
        if (TouchButtonPages[CurrentTouchPageIndex] == null) return;

        if (TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index] == null)
        {
            TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index] = new TouchButton(source.Index);
        }

        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].Text = source.Text;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].TextColor = source.TextColor;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].TextCentered = source.TextCentered;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].TextPositionX = source.TextPositionX;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].TextPositionY = source.TextPositionY;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].TextSize = source.TextSize;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].Image = source.Image;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].BackColor = source.BackColor;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].Command = source.Command;
        TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index].RenderedImage = source.RenderedImage;
    }

    public abstract void InitButtonEvents();

    public abstract SimpleButton CreateSimpleButton(Constants.ButtonType id, Color color, string command);
    protected abstract void SimpleButtonChanged(object sender, EventArgs e);

    public abstract void OnTouchButtonPress(object sender, TouchEventArgs e);

    public abstract void OnRotate(object sender, RotateEventArgs e);

    public abstract void OnSimpleButtonPress(object sender, ButtonEventArgs e);

    protected abstract void TouchItemChanged(object sender, EventArgs e);

    public abstract void StartDeviceThread();

    public abstract void ApplyAllData();

    public abstract void AddRotaryButtonPage();
    public abstract void DeleteRotaryButtonPage();

    public abstract void AddTouchButtonPage();
    public abstract void DeleteTouchButtonPage();

    public abstract void ExceuteSystemCommand(Constants.SystemCommand command);
}