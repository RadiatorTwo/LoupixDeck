using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.SystemPower;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;

    public IAsyncRelayCommand RotaryButtonCommand { get; }
    public IAsyncRelayCommand SimpleButtonCommand { get; }
    public IAsyncRelayCommand TouchButtonCommand { get; }

    public IRelayCommand AddRotaryPageCommand { get; }
    public IRelayCommand DeleteRotaryPageCommand { get; }
    public IRelayCommand RotaryPageButtonCommand { get; }
    public IRelayCommand NextRotaryPageCommand { get; }
    public IRelayCommand PreviousRotaryPageCommand { get; }

    // Side-specific rotary paging — used by the Razer layout, whose two dial columns
    // page independently. Bound per side (Left/Right) in the device layout.
    public IRelayCommand AddLeftRotaryPageCommand { get; }
    public IRelayCommand DeleteLeftRotaryPageCommand { get; }
    public IRelayCommand NextLeftRotaryPageCommand { get; }
    public IRelayCommand PreviousLeftRotaryPageCommand { get; }
    public IRelayCommand AddRightRotaryPageCommand { get; }
    public IRelayCommand DeleteRightRotaryPageCommand { get; }
    public IRelayCommand NextRightRotaryPageCommand { get; }
    public IRelayCommand PreviousRightRotaryPageCommand { get; }

    /// <summary>Opens the free-draw canvas editor for a side strip (Razer). No-op unless
    /// that side is in FreeDraw mode.</summary>
    public IAsyncRelayCommand EditStripCanvasCommand { get; }

    public IRelayCommand AddTouchPageCommand { get; }
    public IRelayCommand DeleteTouchPageCommand { get; }
    public IRelayCommand TouchPageButtonCommand { get; }
    public IRelayCommand NextTouchPageCommand { get; }
    public IRelayCommand PreviousTouchPageCommand { get; }

    public IAsyncRelayCommand SettingsMenuCommand { get; }
    public IAsyncRelayCommand MacroEditorMenuCommand { get; }
    public IAsyncRelayCommand AboutMenuCommand { get; }
    public IRelayCommand QuitApplicationCommand { get; }
    public IRelayCommand ToggleDeviceStateCommand { get; }

    public LoupedeckLiveSController LoupedeckController { get; }

    /// <summary>Slug of the active device — drives MainWindow's device-layout selector.</summary>
    public string DeviceSlug { get; }

    /// <summary>Human label for this device's tab in the shell's device switcher.
    /// Two identical units are disambiguated by a trimmed serial suffix.</summary>
    public string DeviceName { get; }

    /// <summary>Scope key (slug + serial) of this VM's device — lets App match a
    /// <see cref="LoupixDeck.Services.DeviceHost"/> back to its VM on hot-unplug.</summary>
    public string ScopeKey { get; }

    private static string ShortSerial(string serial) =>
        serial.Length <= 8 ? serial : serial[^8..];

    /// <summary>
    /// The shared rotary-knob image. Bound by every dial in both device layouts.
    /// Re-fetched whenever the theme variant changes (see <see cref="OnThemeVariantChanged"/>)
    /// so the knob plastic follows Light/Dark — the underlying bitmap self-heals on read.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Used in model binding")]
    public Avalonia.Media.Imaging.Bitmap RotaryKnobImage => Utils.BitmapHelper.RotaryKnobImage;

    private readonly IDynamicTextManager _dynamicTextManager;
    private readonly Services.Animation.IButtonAnimationManager _buttonAnimationManager;
    private readonly IExclusiveModeService _exclusiveMode;

    /// <summary>
    /// True while a plugin/provider has taken the device over via exclusive mode.
    /// The GUI still shows the configured touch buttons (they aren't what the
    /// device is rendering), so the layouts overlay them with a notice while this
    /// is set. Updated from <see cref="IExclusiveModeService.StateChanged"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExclusiveModeActive { get; private set; }

    /// <summary>Title of the active exclusive-mode provider, shown in the overlay.</summary>
    [ObservableProperty]
    public partial string ExclusiveModeTitle { get; private set; }

    public MainWindowViewModel(LoupedeckLiveSController loupedeck,
        IDialogService dialogService,
        ICommandRegistry commandRegistry,
        IDynamicTextManager dynamicTextManager,
        Services.Animation.IButtonAnimationManager buttonAnimationManager,
        ISystemPowerService powerService,
        IExclusiveModeService exclusiveMode,
        IAppSwitchingService appSwitching,
        LoupedeckConfig config,
        LoupixDeck.Registry.DeviceRegistry.DeviceInfo deviceInfo,
        LoupixDeck.Registry.ResolvedDevice resolved)
    {
        LoupedeckController = loupedeck;
        DeviceSlug = deviceInfo.Slug;
        DeviceName = string.IsNullOrEmpty(resolved?.Serial)
            ? deviceInfo.Name
            : $"{deviceInfo.Name} · {ShortSerial(resolved.Serial)}";
        ScopeKey = resolved?.ScopeKey ?? deviceInfo.Slug;
        _dynamicTextManager = dynamicTextManager;
        _buttonAnimationManager = buttonAnimationManager;
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

        // Foreground-window → page switching. Started on the UI thread because the
        // Windows WinEvent hook requires the message pump of the thread that sets it.
        appSwitching.Start();

        _dialogService = dialogService;

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);

        AddRotaryPageCommand = new RelayCommand(AddRotaryPageButton_Click);
        DeleteRotaryPageCommand = new RelayCommand(DeleteRotaryPageButton_Click);
        RotaryPageButtonCommand = new RelayCommand<int>(RotaryPageButton_Click);
        NextRotaryPageCommand = new RelayCommand(NextRotaryPage_Click);
        PreviousRotaryPageCommand = new RelayCommand(PreviousRotaryPage_Click);

        AddLeftRotaryPageCommand = new RelayCommand(() => AddRotaryPageForSide(RotarySide.Left));
        DeleteLeftRotaryPageCommand = new RelayCommand(() => DeleteRotaryPageForSide(RotarySide.Left));
        NextLeftRotaryPageCommand = new RelayCommand(() => PageRotaryForSide(RotarySide.Left, next: true));
        PreviousLeftRotaryPageCommand = new RelayCommand(() => PageRotaryForSide(RotarySide.Left, next: false));
        AddRightRotaryPageCommand = new RelayCommand(() => AddRotaryPageForSide(RotarySide.Right));
        DeleteRightRotaryPageCommand = new RelayCommand(() => DeleteRotaryPageForSide(RotarySide.Right));
        NextRightRotaryPageCommand = new RelayCommand(() => PageRotaryForSide(RotarySide.Right, next: true));
        PreviousRightRotaryPageCommand = new RelayCommand(() => PageRotaryForSide(RotarySide.Right, next: false));

        EditStripCanvasCommand = new AsyncRelayCommand<RotarySide>(EditStripCanvas_Click);

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

    /// <summary>
    /// Detaches the process-global event subscriptions this VM holds (theme variant,
    /// exclusive-mode) so an unplugged device's VM stops reacting and can be collected
    /// (issue #116 phase 3b). The power/app-switching subscriptions can't be undone
    /// (lambdas / no Stop API); they fire harmlessly against the now-closed device.
    /// </summary>
    public void Detach()
    {
        if (Avalonia.Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeVariantChanged;
        _exclusiveMode.StateChanged -= OnExclusiveModeStateChanged;
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

    private void AddRotaryPageForSide(RotarySide side)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.AddRotaryButtonPage(side);
            LoupedeckController.SaveConfig();
        });
    }

    private void DeleteRotaryPageForSide(RotarySide side)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.DeleteRotaryButtonPage(side);
            LoupedeckController.SaveConfig();
        });
    }

    private void PageRotaryForSide(RotarySide side, bool next)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (next)
                LoupedeckController.PageManager.NextRotaryPage(side);
            else
                LoupedeckController.PageManager.PreviousRotaryPage(side);
        });
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
        await _dialogService.ShowDialogAsync<RotaryButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button));

        LoupedeckController.SaveConfig();

        // Refresh the side strip so a command change on this dial (e.g. assigning an audio
        // command) updates its segment immediately. Resolve which side the dial belongs to;
        // RefreshSideStrip is a no-op on devices without side strips.
        var pageManager = LoupedeckController.PageManager;
        foreach (var side in new[] { RotarySide.Left, RotarySide.Right })
        {
            if (pageManager.GetCurrentRotaryPage(side)?.RotaryButtons?.Contains(button) == true)
            {
                await LoupedeckController.RefreshSideStrip(side);
                break;
            }
        }
    }

    private async Task SimpleButton_Click(SimpleButton button)
    {
        await _dialogService.ShowDialogAsync<SimpleButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button));

        LoupedeckController.SaveConfig();
    }

    private async Task TouchButton_Click(TouchButton button)
    {
        await _dialogService.ShowDialogAsync<TouchButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button));

        LoupedeckController.SaveConfig();
        _dynamicTextManager.Rescan();
        _buttonAnimationManager.Rescan();
    }

    /// <summary>
    /// Opens the layer editor on the current rotary page's strip canvas (60×270).
    /// Always available — the canvas is editable regardless of the page's
    /// <see cref="StripMode"/>; the mode only controls whether that canvas is shown
    /// on the device (FreeDraw) or replaced by the auto dial labels (Segmented).
    /// </summary>
    private async Task EditStripCanvas_Click(RotarySide side)
    {
        var page = LoupedeckController.PageManager.GetCurrentRotaryPage(side);
        if (page == null) return;

        // The canvas is a TouchButton reused as a 60×270 layer surface; create it lazily.
        page.StripCanvas ??= new TouchButton(
            side == RotarySide.Left
                ? LoupixDeck.LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex
                : LoupixDeck.LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex);

        // Wire the canvas into the live-redraw pipeline so layer edits paint the strip
        // immediately, just like grid touch buttons (instead of only on dialog close).
        LoupedeckController.RegisterStripCanvas(page);

        await _dialogService.ShowDialogAsync<TouchButtonSettingsViewModel, DialogResult>(vm =>
        {
            vm.SetCanvasSize(60, 270);
            vm.ConfigureStrip(page);
            vm.Initialize(page.StripCanvas);
        });

        LoupedeckController.SaveConfig();
        await LoupedeckController.RefreshSideStrip(side);
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