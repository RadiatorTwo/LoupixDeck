using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.SystemPower;
using LoupixDeck.ViewModels.Base;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using RelayCommand = LoupixDeck.Utils.RelayCommand;

namespace LoupixDeck.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;

    public ICommand RotaryButtonCommand { get; }
    public ICommand SimpleButtonCommand { get; }
    public ICommand TouchButtonCommand { get; }

    public ICommand AddRotaryPageCommand { get; }
    public ICommand DeleteRotaryPageCommand { get; }
    public ICommand RotaryPageButtonCommand { get; }
    public ICommand NextRotaryPageCommand { get; }
    public ICommand PreviousRotaryPageCommand { get; }


    public ICommand AddTouchPageCommand { get; }
    public ICommand DeleteTouchPageCommand { get; }
    public ICommand TouchPageButtonCommand { get; }
    public ICommand NextTouchPageCommand { get; }
    public ICommand PreviousTouchPageCommand { get; }

    public ICommand SettingsMenuCommand { get; }
    public ICommand MacroEditorMenuCommand { get; }
    public ICommand AboutMenuCommand { get; }
    public ICommand QuitApplicationCommand { get; }
    public ICommand ToggleDeviceStateCommand { get; }

    public LoupedeckLiveSController LoupedeckController { get; }

    /// <summary>Slug of the active device — drives MainWindow's device-layout selector.</summary>
    public string DeviceSlug { get; }

    /// <summary>
    /// The shared rotary-knob image. Bound by every dial in both device layouts.
    /// Re-fetched whenever the theme variant changes (see <see cref="OnThemeVariantChanged"/>)
    /// so the knob plastic follows Light/Dark — the underlying bitmap self-heals on read.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap RotaryKnobImage => Utils.BitmapHelper.RotaryKnobImage;

    private readonly IDynamicTextManager _dynamicTextManager;
    private readonly IExclusiveModeService _exclusiveMode;

    private bool _isExclusiveModeActive;

    /// <summary>
    /// True while a plugin/provider has taken the device over via exclusive mode.
    /// The GUI still shows the configured touch buttons (they aren't what the
    /// device is rendering), so the layouts overlay them with a notice while this
    /// is set. Updated from <see cref="IExclusiveModeService.StateChanged"/>.
    /// </summary>
    public bool IsExclusiveModeActive
    {
        get => _isExclusiveModeActive;
        private set => SetProperty(ref _isExclusiveModeActive, value);
    }

    private string _exclusiveModeTitle;

    /// <summary>Title of the active exclusive-mode provider, shown in the overlay.</summary>
    public string ExclusiveModeTitle
    {
        get => _exclusiveModeTitle;
        private set => SetProperty(ref _exclusiveModeTitle, value);
    }

    public MainWindowViewModel(LoupedeckLiveSController loupedeck,
        IDialogService dialogService,
        ICommandRegistry commandRegistry,
        IDynamicTextManager dynamicTextManager,
        ISystemPowerService powerService,
        IExclusiveModeService exclusiveMode,
        LoupedeckConfig config,
        LoupixDeck.Registry.DeviceRegistry.DeviceInfo deviceInfo)
    {
        LoupedeckController = loupedeck;
        DeviceSlug = deviceInfo.Slug;
        _dynamicTextManager = dynamicTextManager;
        _exclusiveMode = exclusiveMode;

        commandRegistry.Initialize();

        // Mirror exclusive-mode state into bindable properties. StateChanged can
        // fire off the UI thread (controller / UDP worker), so marshal before
        // touching the observable properties the layouts bind to.
        _exclusiveMode.StateChanged += OnExclusiveModeStateChanged;
        OnExclusiveModeStateChanged();

        // Auto-clear the device while the host is suspended, restore on wake.
        // Both handlers must hop to the UI thread because they touch ObservableCollections
        // (TouchButtons / SimpleButtons) that the UI binds to.
        powerService.Suspending += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoupedeckController.ClearDeviceState());
        powerService.Resuming += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                // Give the USB stack a moment to re-enumerate the device after wake.
                await Task.Delay(1000);
                await LoupedeckController.RestoreDeviceState();
            });
        powerService.StartMonitoring();

        _dialogService = dialogService;

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);

        AddRotaryPageCommand = new RelayCommand(AddRotaryPageButton_Click);
        DeleteRotaryPageCommand = new RelayCommand(DeleteRotaryPageButton_Click);
        RotaryPageButtonCommand = new RelayCommand<int>(RotaryPageButton_Click);
        NextRotaryPageCommand = new RelayCommand(NextRotaryPage_Click);
        PreviousRotaryPageCommand = new RelayCommand(PreviousRotaryPage_Click);

        AddTouchPageCommand = new RelayCommand(AddTouchPageButton_Click);
        DeleteTouchPageCommand = new RelayCommand(DeleteTouchPageButton_Click);
        TouchPageButtonCommand = new RelayCommand<int>(TouchPageButton_Click);
        NextTouchPageCommand = new RelayCommand(NextTouchPage_Click);
        PreviousTouchPageCommand = new RelayCommand(PreviousTouchPage_Click);

        SettingsMenuCommand = new AsyncRelayCommand(SettingsMenuButton_Click);
        MacroEditorMenuCommand = new AsyncRelayCommand(MacroEditorMenuButton_Click);
        AboutMenuCommand = new AsyncRelayCommand(AboutMenuButton_Click);
        QuitApplicationCommand = new RelayCommand(QuitApplication);
        ToggleDeviceStateCommand = new AsyncRelayCommand(LoupedeckController.ToggleDeviceState);

        // Follow Light/Dark for the rendered device chrome (knob + LED/RGB buttons),
        // whose bitmaps bake in their colours and so can't react to DynamicResource.
        if (Avalonia.Application.Current is { } currentApp)
            currentApp.ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    private void OnThemeVariantChanged(object sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // The knob bitmap self-heals to the new theme on next read; nudge the binding.
            OnPropertyChanged(nameof(RotaryKnobImage));
            // LED/RGB button bodies are baked bitmaps — re-render them for the new theme.
            LoupedeckController.RefreshRenderedButtonChrome();
        });
    }

    private void OnExclusiveModeStateChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsExclusiveModeActive = _exclusiveMode.IsActive;
            ExclusiveModeTitle = _exclusiveMode.Current?.Title ?? string.Empty;
        });
    }

    private void AddRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.AddRotaryButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void DeleteRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.DeleteRotaryButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void RotaryPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.ApplyRotaryPage(page - 1);
        });
    }

    private void NextRotaryPage_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LoupedeckController.PageManager.NextRotaryPage());
    }

    private void PreviousRotaryPage_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LoupedeckController.PageManager.PreviousRotaryPage());
    }

    private void AddTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.AddTouchButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void DeleteTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.DeleteTouchButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void TouchPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.ApplyTouchPage(page - 1);
        });
    }

    private void NextTouchPage_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _ = LoupedeckController.PageManager.NextTouchPage());
    }

    private void PreviousTouchPage_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _ = LoupedeckController.PageManager.PreviousTouchPage());
    }

    private async Task RotaryButton_Click(RotaryButton button)
    {
        await _dialogService.ShowDialogAsync<RotaryButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
    }

    private async Task SimpleButton_Click(SimpleButton button)
    {
        await _dialogService.ShowDialogAsync<SimpleButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
    }

    private async Task TouchButton_Click(TouchButton button)
    {
        await _dialogService.ShowDialogAsync<TouchButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
        _dynamicTextManager.Rescan();
    }

    private async Task SettingsMenuButton_Click()
    {
        await _dialogService.ShowDialogAsync<SettingsViewModel, DialogResult>();
        LoupedeckController.SaveConfig();
    }

    private async Task MacroEditorMenuButton_Click()
    {
        // Macros persist in their own macros.json — no SaveConfig needed here.
        await _dialogService.ShowDialogAsync<MacroEditorViewModel, DialogResult>();
    }
    
    private async Task AboutMenuButton_Click()
    {
        await _dialogService.ShowDialogAsync<AboutViewModel, DialogResult>();
        LoupedeckController.SaveConfig();
    }

    private void QuitApplication()
    {
        var window = Utils.WindowHelper.GetMainWindow();
        if (window is Views.MainWindow mw)
        {
            mw.QuitApplication();
            return;
        }
        Environment.Exit(0);
    }
}