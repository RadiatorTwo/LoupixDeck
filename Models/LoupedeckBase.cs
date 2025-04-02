using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoMapper;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Services;
using Newtonsoft.Json;
using LoupixDeck.Models.Converter;

namespace LoupixDeck.Models;

public abstract class LoupedeckBase : INotifyPropertyChanged
{
    private int _currentTouchPageIndex;

    [JsonIgnore]
    public int CurrentTouchPageIndex
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
    public int CurrentRotaryPageIndex
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
    protected ObsController Obs;
    protected DBusController DBus;

    private readonly ElgatoController _elgatoController;
    private readonly IMapper _mapper;

    public ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; }
    public ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; }
    public SimpleButton[] SimpleButtons { get; set; }

    [JsonIgnore] public TouchButton[] CurrentTouchButtonPage { get; set; }

    [JsonIgnore] private ElgatoDevices ElgatoDevices { get; set; }

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

    protected LoupedeckBase(ObsController obs,
        DBusController dbus,
        ElgatoController elgatoController,
        ElgatoDevices elgatoDevices,
        CommandRunner runner,
        IMapper mapper)
    {
        Obs = obs;
        obs.Connect();
        DBus = dbus;
        CommandRunner = runner;
        _mapper = mapper;
        ElgatoDevices = elgatoDevices;
        _elgatoController = elgatoController;

        _elgatoController.KeyLightFound += (_, light) =>
        {
            var checkDevice = ElgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == light.DisplayName);

            // We remove an existing KeyLight, to be able to re add it, in case the devices ip has changed.
            if (checkDevice != null)
            {
                ElgatoDevices.RemoveKeyLight(checkDevice);
            }

            _elgatoController.InitDeviceAsync(light).GetAwaiter().GetResult();
            ElgatoDevices.AddKeyLight(light);
        };

        _ = _elgatoController.ProbeForElgatoDevices();
    }

    public void SaveToFile()
    {
        // Copy all Current Touchbuttons to TouchButtonPage before saving.
        CopyCurrentTouchButtonsToPage();
        
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };

        settings.Converters.Add(new ColorJsonConverter());
        settings.Converters.Add(new BitmapJsonConverter());

        var json = JsonConvert.SerializeObject(this, settings);
        var filePath = FileDialogHelper.GetConfigPath("config.json");
        File.WriteAllText(filePath, json);
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

    public void NextRotaryPage()
    {
        CurrentRotaryPageIndex = (CurrentRotaryPageIndex + 1) % RotaryButtonPages.Count;
    }

    public void PreviousRotaryPage()
    {
        CurrentRotaryPageIndex = (CurrentRotaryPageIndex - 1 + RotaryButtonPages.Count) % RotaryButtonPages.Count;
    }

    public void ApplyRotaryPage(int pageIndex)
    {
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
        // Copy the TouchButtons of the new page to `CurrentTouchButtons`.
        foreach (var touchButton in TouchButtonPages[pageIndex].TouchButtons)
        {
            CopyTouchButtonData(touchButton);
        }

        CurrentTouchPageIndex = pageIndex;
    }

    private void CopyCurrentTouchButtonsToPage()
    {
        foreach (var currentTouchButton in CurrentTouchButtonPage)
        {
            CopyBackTouchButtonData(currentTouchButton);
        }
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

        _mapper.Map(source, CurrentTouchButtonPage[source.Index]);

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

        _mapper.Map(source, TouchButtonPages[CurrentTouchPageIndex].TouchButtons[source.Index]);
    }

    public abstract void InitDevice();

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
}