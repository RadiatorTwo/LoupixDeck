using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Utils;
using System.Text.Json;

namespace LoupixDeck.Models;

public abstract class LoupedeckBase
{
    public readonly AutoResetEvent DeviceCreatedEvent = new(false);

    public List<TouchButton[]> TouchButtonPages { get; set; }

    public int CurrentPageIndex { get; set; } = -1;
    public TouchButton[] CurrentTouchButtonPage { get; set; }

    public SimpleButton[] SimpleButtons { get; set; }
    public RotaryButton[] RotaryButtons { get; set; }

    private double _brightness = 1;

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (value.Equals(_brightness)) return;

            _brightness = value;
            StaticDevice.Device.SetBrightness(_brightness);
        }
    }

    public virtual void SaveToFile()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new ColorJsonConverter(), new BitmapJsonConverter() }
        };

        var json = JsonSerializer.Serialize(this, options);
        var filePath = GetConfigPath("LoupixDeck", "config.json");
        File.WriteAllText(filePath, json);
    }

    public static T LoadFromFile<T>() where T : LoupedeckBase
    {
        var filePath = GetConfigPath("LoupixDeck", "config.json");

        if (!File.Exists(filePath))
            return null;

        JsonSerializerOptions options = new()
        {
            Converters = { new ColorJsonConverter(), new BitmapJsonConverter() }
        };

        var json = File.ReadAllText(filePath);

        var instance = JsonSerializer.Deserialize<T>(json, options);

        instance.InitUpdateEvents();

        return instance;
    }

    public void InitUpdateEvents()
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

    public static string GetConfigPath(string appName, string fileName)
    {
        var homePath = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var configDir = Path.Combine(homePath, ".config", appName);

        // Falls das Verzeichnis nicht existiert, erstelle es
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        return Path.Combine(configDir, fileName);
    }

    public void NextPage()
    {
        CurrentPageIndex = (CurrentPageIndex + 1) % TouchButtonPages.Count;
        ApplyPage(CurrentPageIndex);
    }

    public void PreviousPage()
    {
        CurrentPageIndex = (CurrentPageIndex - 1 + TouchButtonPages.Count) % TouchButtonPages.Count;
        ApplyPage(CurrentPageIndex);
    }

    public void ApplyPage(int pageIndex)
    {
        // Copy the TouchButtons of the new page to `CurrentTouchButtons`.
        foreach (var touchButton in TouchButtonPages[pageIndex])
        {
            CopyTouchButtonData(touchButton);
        }
    }

    public void RefreshTouchButtons()
    {
        foreach (var touchButton in CurrentTouchButtonPage)
        {
            touchButton.Refresh();
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
        CurrentTouchButtonPage[source.Index].TextPosition = source.TextPosition;
        CurrentTouchButtonPage[source.Index].TextSize = source.TextSize;
        CurrentTouchButtonPage[source.Index].Image = source.Image;
        CurrentTouchButtonPage[source.Index].BackColor = source.BackColor;
        CurrentTouchButtonPage[source.Index].Command = source.Command;
        CurrentTouchButtonPage[source.Index].RenderedImage = source.RenderedImage;

        CurrentTouchButtonPage[source.Index].IgnoreRefresh = false;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => { CurrentTouchButtonPage[source.Index].Refresh(); });
    }
    
    public void CopyBackTouchButtonData(TouchButton source)
    {
        // Check if Page exists.
        if (TouchButtonPages[CurrentPageIndex] == null) return;

        if (TouchButtonPages[CurrentPageIndex][source.Index] == null)
        {
            TouchButtonPages[CurrentPageIndex][source.Index] = new TouchButton(source.Index);
        }
        
        TouchButtonPages[CurrentPageIndex][source.Index].Text = source.Text;
        TouchButtonPages[CurrentPageIndex][source.Index].TextColor = source.TextColor;
        TouchButtonPages[CurrentPageIndex][source.Index].TextCentered = source.TextCentered;
        TouchButtonPages[CurrentPageIndex][source.Index].TextPosition = source.TextPosition;
        TouchButtonPages[CurrentPageIndex][source.Index].TextSize = source.TextSize;
        TouchButtonPages[CurrentPageIndex][source.Index].Image = source.Image;
        TouchButtonPages[CurrentPageIndex][source.Index].BackColor = source.BackColor;
        TouchButtonPages[CurrentPageIndex][source.Index].Command = source.Command;
        TouchButtonPages[CurrentPageIndex][source.Index].RenderedImage = source.RenderedImage;
    }

    public abstract void InitButtonEvents();

    public abstract SimpleButton CreateSimpleButton(string id, Color color, string command);
    public abstract void SimpleButtonChanged(object sender, EventArgs e);

    public abstract void OnTouchButtonPress(object sender, TouchEventArgs e);

    public abstract void OnRotate(object sender, RotateEventArgs e);

    public abstract void OnSimpleButtonPress(object sender, ButtonEventArgs e);

    public abstract void TouchItemChanged(object sender, EventArgs e);

    public abstract void StartDeviceThread();

    public abstract void ApplyAllData();

    public abstract void AddPage();

    public abstract void ExceuteSystemCommand(Constants.SystemCommand command);
}