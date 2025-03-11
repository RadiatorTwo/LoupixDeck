using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Utils;
using System.Text.Json;

namespace LoupixDeck.Models;

public abstract class LoupedeckBase
{
    public readonly AutoResetEvent DeviceCreatedEvent = new(false);
    
    public List<TouchButton[]> TouchButtonPages { get; set; }
    
    public int CurrentPageIndex { get; set; } = 0;
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
        return JsonSerializer.Deserialize<T>(json, options);
    }
    
    public static string GetConfigPath(string appName, string fileName)
    {
        string homePath = Environment.GetEnvironmentVariable("HOME") 
                          ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        string configDir = Path.Combine(homePath, ".config", appName);

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
        for (int i = 0; i < 15; i++)
        {
            CurrentTouchButtonPage[i] = TouchButtonPages[pageIndex][i];

            // Set the appropriate image for the TouchButton
            CurrentTouchButtonPage[i].Refresh();
        }
    }

    public abstract SimpleButton CreateSimpleButton(string id, Color color);
    public abstract void SimpleButtonChanged(object sender, EventArgs e);

    public abstract void ButtonTouched(object sender, TouchEventArgs e);

    public abstract void Rotated(object sender, RotateEventArgs e);

    public abstract void ButtonPressed(object sender, ButtonEventArgs e);

    public abstract void TouchItemChanged(object sender, EventArgs e);

    public abstract void StartDeviceThread();

    public abstract void ApplyAllData();
}