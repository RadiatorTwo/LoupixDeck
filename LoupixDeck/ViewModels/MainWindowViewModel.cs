using System.Collections.ObjectModel;
using System.Windows.Input;
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

/// <summary>What a drag &amp; drop would do at the current target (issue #166 phase 3).</summary>
public enum DropOperation
{
    None,
    Move,
    Swap,
    Copy
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly IButtonClipboardService _clipboard;
    private readonly IWorkspaceActivationService _workspaceActivation;
    private readonly LoupedeckConfig _config;

    // Guards the profile/workspace ComboBox selection against feedback loops: set while we
    // push an external activation (context rules, commands, buttons) back into the bound
    // SelectedProfile/SelectedWorkspace, so their OnChanged hooks don't re-activate.
    private bool _suppressActivationSync;

    // The two side displays occupy touch-button slots 12/13 on devices with side strips.
    private const int LeftSideIndex = LoupixDeck.LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex;
    private const int RightSideIndex = LoupixDeck.LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex;

    public IAsyncRelayCommand RotaryButtonCommand { get; }
    public IAsyncRelayCommand SimpleButtonCommand { get; }
    public IAsyncRelayCommand TouchButtonCommand { get; }

    // Copy / cut / paste / clear of the selected button or side display (issue #166).
    public IRelayCommand CopySelectedCommand { get; }
    public IRelayCommand CutSelectedCommand { get; }
    public IAsyncRelayCommand PasteSelectedCommand { get; }
    public IRelayCommand ClearSelectedCommand { get; }

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
    public Avalonia.Media.Imaging.Bitmap RotaryKnobImage => Utils.BitmapHelper.RotaryKnobImage;

    private readonly IDynamicTextManager _dynamicTextManager;
    private readonly Services.Animation.IButtonAnimationManager _buttonAnimationManager;
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

    /// <summary>Title of the active exclusive-mode provider, shown in the overlay.</summary>

    [ObservableProperty]
    public partial string ExclusiveModeTitle { get; private set; }

    // ─────────────────────────── Profile / workspace switcher (issue #132) ───────────────────────────

    /// <summary>The profiles configured on this device — the source for the header's Profile
    /// dropdown. Mutating this collection in Settings (add/rename/remove) updates the dropdown live.</summary>
    public ObservableCollection<Profile> Profiles => _config.Profiles;

    private Profile _selectedProfile;

    /// <summary>Selected profile in the header dropdown. Bound two-way; picking a new one activates it
    /// (opening its home workspace). Kept in sync with external activations via
    /// <see cref="IWorkspaceActivationService.ActiveProfileChanged"/>.</summary>
    public Profile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            // Switching the active device swaps this ComboBox's ItemsSource; Avalonia clears its
            // SelectedItem to null in the process, and the TwoWay binding would write that null
            // back — wiping the device's stored selection. Ignore the transient null while the
            // current selection is still a valid member of Profiles (a device switch), and
            // re-assert it so the ComboBox rebinds. A real profile change (non-null, or the old
            // value no longer in the list) passes straight through.
            if (value == null && _selectedProfile != null && Profiles != null && Profiles.Contains(_selectedProfile))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedProfile)));
                return;
            }

            if (!SetProperty(ref _selectedProfile, value)) return;
            OnPropertyChanged(nameof(Workspaces));

            // Only user-driven dropdown changes activate; syncing from an external activation is suppressed.
            if (!_suppressActivationSync && value != null)
                _ = _workspaceActivation.ActivateProfile(value.Id);
        }
    }

    /// <summary>Workspaces of the <see cref="SelectedProfile"/> — the source for the header's
    /// Workspace dropdown. Re-projected whenever the selected profile changes.</summary>
    public ObservableCollection<Workspace> Workspaces => SelectedProfile?.Workspaces;

    private Workspace _selectedWorkspace;

    /// <summary>Selected workspace in the header dropdown. Bound two-way; picking a new one activates
    /// it within the current profile. Kept in sync with external activations via
    /// <see cref="IWorkspaceActivationService.ActiveWorkspaceChanged"/>.</summary>
    public Workspace SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            // Same transient-null guard as SelectedProfile: reject the null the ComboBox writes
            // when its ItemsSource is swapped on a device switch (current workspace still valid),
            // but allow it when the profile changed (old workspace no longer in the new list) so
            // the activation event can move us to the new home workspace.
            ObservableCollection<Workspace> workspaces = Workspaces;
            if (value == null && _selectedWorkspace != null && workspaces != null && workspaces.Contains(_selectedWorkspace))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedWorkspace)));
                return;
            }

            if (!SetProperty(ref _selectedWorkspace, value)) return;

            if (!_suppressActivationSync && value != null)
                _ = _workspaceActivation.ActivateWorkspace(value.Id);
        }
    }

    private void OnActiveProfileChanged(Profile profile)
    {
        // Activation can be raised off the UI thread (context engine / app-switching worker).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _suppressActivationSync = true;
            SelectedProfile = profile;
            _suppressActivationSync = false;
        });
    }

    private void OnActiveWorkspaceChanged(Workspace workspace)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _suppressActivationSync = true;
            SelectedWorkspace = workspace;
            _suppressActivationSync = false;
        });
    }

    public MainWindowViewModel(LoupedeckLiveSController loupedeck,
        IDialogService dialogService,
        IButtonClipboardService clipboard,
        ICommandRegistry commandRegistry,
        IDynamicTextManager dynamicTextManager,
        Services.Animation.IButtonAnimationManager buttonAnimationManager,
        ISystemPowerService powerService,
        IExclusiveModeService exclusiveMode,
        IAppSwitchingService appSwitching,
        IWorkspaceActivationService workspaceActivation,
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
        _workspaceActivation = workspaceActivation;
        _config = config;

        // Seed the header dropdowns from the current active profile/workspace, and keep them in
        // sync with activations that originate elsewhere (context rules, commands, device buttons).
        // Suppressed so the initial assignment doesn't re-trigger an activation.
        _suppressActivationSync = true;
        SelectedProfile = _workspaceActivation.ActiveProfile;
        SelectedWorkspace = _workspaceActivation.ActiveWorkspace;
        _suppressActivationSync = false;
        _workspaceActivation.ActiveProfileChanged += OnActiveProfileChanged;
        _workspaceActivation.ActiveWorkspaceChanged += OnActiveWorkspaceChanged;

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
        _clipboard = clipboard;

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);

        CopySelectedCommand = new RelayCommand(CopySelected);
        CutSelectedCommand = new RelayCommand(CutSelected);
        PasteSelectedCommand = new AsyncRelayCommand(PasteSelected);
        ClearSelectedCommand = new RelayCommand(ClearSelected);

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
        _workspaceActivation.ActiveProfileChanged -= OnActiveProfileChanged;
        _workspaceActivation.ActiveWorkspaceChanged -= OnActiveWorkspaceChanged;
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
        LoupedeckController.AnimateGotoRotaryPage(page - 1);
    }

    private void NextRotaryPage_Click()
    {
        LoupedeckController.AnimateNextRotaryPage();
    }

    private void PreviousRotaryPage_Click()
    {
        LoupedeckController.AnimatePreviousRotaryPage();
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
        LoupedeckController.AnimateRotaryPageForSide(side, next);
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
        LoupedeckController.AnimateGotoTouchPage(page - 1);
    }

    private void NextTouchPage_Click()
    {
        LoupedeckController.AnimateNextTouchPage();
    }

    private void PreviousTouchPage_Click()
    {
        LoupedeckController.AnimatePreviousTouchPage();
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

    private LoupedeckButton _selectedButton;

    /// <summary>
    /// Single-click selection in the device layout view. Highlights exactly one button at a
    /// time (the layout shows the selection as a border); selecting a new button clears the
    /// previous one. UI-only, not persisted.
    /// </summary>
    public void SelectButton(LoupedeckButton button)
    {
        if (ReferenceEquals(_selectedButton, button)) return;

        if (_selectedButton != null)
            _selectedButton.IsSelected = false;

        _selectedButton = button;

        if (button != null)
            button.IsSelected = true;
    }

    // ─────────────────────────── Copy / cut / paste / clear (issue #166) ───────────────────────────

    /// <summary>Classifies the selected element for clipboard compatibility. A side-strip touch
    /// button (slot 12/13 on a device with side displays) is <see cref="ButtonKind.SideDisplay"/>,
    /// not <see cref="ButtonKind.Touch"/> — its content lives on the rotary page's strip canvas.</summary>
    private ButtonKind? Classify(LoupedeckButton button)
    {
        switch (button)
        {
            case TouchButton touch when IsSideStripButton(touch):
                return ButtonKind.SideDisplay;
            case TouchButton:
                return ButtonKind.Touch;
            case SimpleButton:
                return ButtonKind.Simple;
            case RotaryButton:
                return ButtonKind.Rotary;
            default:
                return null;
        }
    }

    private bool IsSideStripButton(TouchButton button)
        => LoupedeckController.HasSideStrips &&
           (button.Index == LeftSideIndex || button.Index == RightSideIndex);

    private static RotarySide SideOf(TouchButton stripButton)
        => stripButton.Index == RightSideIndex ? RotarySide.Right : RotarySide.Left;

    /// <summary>The strip canvas (a 60×270 <see cref="TouchButton"/>) that backs the side display
    /// on <paramref name="side"/>, created lazily like the strip editor does.</summary>
    private TouchButton EnsureStripCanvas(RotarySide side, out RotaryButtonPage page)
    {
        page = LoupedeckController.PageManager.GetCurrentRotaryPage(side);
        if (page == null) return null;
        page.StripCanvas ??= new TouchButton(side == RotarySide.Left ? LeftSideIndex : RightSideIndex);
        return page.StripCanvas;
    }

    public bool CanCopySelected() => Classify(_selectedButton) != null;

    public bool CanPasteSelected()
    {
        ButtonKind? kind = Classify(_selectedButton);
        return kind != null && _clipboard.CanPasteInto(kind.Value);
    }

    public bool CanClearSelected() => HasContent(_selectedButton);

    /// <summary>True when the element currently holds user configuration (for a side display, its
    /// strip canvas). Used to gate Clear and to disallow picking up an empty button in drag &amp;
    /// drop.</summary>
    private bool HasContent(LoupedeckButton button)
    {
        ButtonKind? kind = Classify(button);
        if (kind == null) return false;

        if (kind == ButtonKind.SideDisplay)
        {
            RotaryButtonPage page = LoupedeckController.PageManager.GetCurrentRotaryPage(SideOf((TouchButton)button));
            return page?.StripCanvas != null && !_clipboard.IsEmpty(page.StripCanvas);
        }

        return !_clipboard.IsEmpty(button);
    }

    private void CopySelected()
    {
        LoupedeckButton button = _selectedButton;
        ButtonKind? kind = Classify(button);
        if (kind == null) return;

        if (kind == ButtonKind.SideDisplay)
        {
            TouchButton canvas = EnsureStripCanvas(SideOf((TouchButton)button), out _);
            if (canvas == null) return;
            _clipboard.Copy(canvas, ButtonKind.SideDisplay);
            return;
        }

        _clipboard.Copy(button, kind.Value);
    }

    private void CutSelected()
    {
        if (!CanClearSelected()) return;
        CopySelected();
        ClearSelected();
    }

    private async Task PasteSelected()
    {
        LoupedeckButton button = _selectedButton;
        ButtonKind? kind = Classify(button);
        if (kind == null || !_clipboard.CanPasteInto(kind.Value)) return;

        if (kind == ButtonKind.SideDisplay)
        {
            TouchButton canvas = EnsureStripCanvas(SideOf((TouchButton)button), out RotaryButtonPage page);
            if (canvas == null) return;
            if (!_clipboard.IsEmpty(canvas) && !await ConfirmOverwrite()) return;

            _clipboard.PasteInto(canvas);
            LoupedeckController.RegisterStripCanvas(page);
            LoupedeckController.SaveConfig();
            await LoupedeckController.RefreshSideStrip(SideOf((TouchButton)button));
            return;
        }

        if (!_clipboard.IsEmpty(button) && !await ConfirmOverwrite()) return;

        _clipboard.PasteInto(button);

        switch (kind)
        {
            case ButtonKind.Touch:
                PostTouchChange();
                break;
            case ButtonKind.Simple:
                LoupedeckController.SaveConfig();
                break;
            case ButtonKind.Rotary:
                LoupedeckController.SaveConfig();
                await RefreshRotarySide((RotaryButton)button);
                break;
        }
    }

    private void ClearSelected()
    {
        LoupedeckButton button = _selectedButton;
        ButtonKind? kind = Classify(button);
        if (kind == null) return;

        switch (kind)
        {
            case ButtonKind.Touch:
                ClearTouchContent((TouchButton)button);
                PostTouchChange();
                break;

            case ButtonKind.Simple:
                ClearSimpleContent((SimpleButton)button);
                LoupedeckController.SaveConfig();
                break;

            case ButtonKind.Rotary:
                ClearRotaryContent((RotaryButton)button);
                LoupedeckController.SaveConfig();
                _ = RefreshRotarySide((RotaryButton)button);
                break;

            case ButtonKind.SideDisplay:
                RotarySide side = SideOf((TouchButton)button);
                TouchButton canvas = EnsureStripCanvas(side, out RotaryButtonPage page);
                if (canvas == null) return;
                ClearTouchContent(canvas);
                LoupedeckController.RegisterStripCanvas(page);
                LoupedeckController.SaveConfig();
                _ = LoupedeckController.RefreshSideStrip(side);
                break;
        }
    }

    /// <summary>Persist + rebuild the dynamic-text / animation entry sets after a touch button's
    /// content changed (mirrors <see cref="TouchButton_Click"/>).</summary>
    private void PostTouchChange()
    {
        LoupedeckController.SaveConfig();
        _dynamicTextManager.Rescan();
        _buttonAnimationManager.Rescan();
    }

    /// <summary>Repaint the side strip the given dial belongs to (a rotary command drives its
    /// segment label). No-op on devices without side strips.</summary>
    private async Task RefreshRotarySide(RotaryButton rotary)
    {
        IPageManager pageManager = LoupedeckController.PageManager;
        foreach (RotarySide side in new[] { RotarySide.Left, RotarySide.Right })
        {
            if (pageManager.GetCurrentRotaryPage(side)?.RotaryButtons?.Contains(rotary) == true)
            {
                await LoupedeckController.RefreshSideStrip(side);
                break;
            }
        }
    }

    private async Task<bool> ConfirmOverwrite()
    {
        DialogResult result = await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(
                "This button already contains a configuration. Do you want to overwrite it?",
                title: "Overwrite?",
                confirmText: "Overwrite",
                cancelText: "Cancel"));
        return result.IsConfirmed;
    }

    // Collapse a touch/strip button to a single empty default state (keeps the instance, so its
    // ItemChanged subscription survives), then repaint.
    private static void ClearTouchContent(TouchButton button)
    {
        while (button.States.Count > 1)
            button.States.RemoveAt(button.States.Count - 1);

        ButtonState state = button.States[0];
        state.Layers.Clear();
        state.BackColor = Avalonia.Media.Colors.Black;
        state.LedColor = Avalonia.Media.Colors.Black;
        state.Command = null;
        state.VibrationEnabled = false;

        button.DefaultStateId = state.Id;
        button.Command = null;
        button.SetActiveState(state.Id);
    }

    private static void ClearSimpleContent(SimpleButton button)
    {
        while (button.States.Count > 1)
            button.States.RemoveAt(button.States.Count - 1);

        ButtonState state = button.States[0];
        state.LedColor = Avalonia.Media.Colors.Black;
        state.Command = null;

        button.DefaultStateId = state.Id;
        button.Command = null;
        button.SetActiveState(state.Id);
    }

    private static void ClearRotaryContent(RotaryButton button)
    {
        button.Command = null;
        button.RotaryLeftCommand = string.Empty;
        button.RotaryRightCommand = string.Empty;
        button.DisplayText = string.Empty;
        button.Refresh();
    }

    // ─────────────────────────── Drag & drop (issue #166) ───────────────────────────

    /// <summary>True when this element can be picked up for a drag — only non-empty buttons /
    /// side displays, so an empty slot can't be dragged around and cause accidental swaps.</summary>
    public bool CanDrag(LoupedeckButton button) => HasContent(button);

    /// <summary>True when <paramref name="source"/> can be dropped onto <paramref name="target"/>
    /// (same kind, different element).</summary>
    public bool CanDropOnto(LoupedeckButton source, LoupedeckButton target)
    {
        if (source == null || target == null || ReferenceEquals(source, target)) return false;
        ButtonKind? s = Classify(source);
        ButtonKind? t = Classify(target);
        return s != null && s == t;
    }

    /// <summary>What dropping <paramref name="source"/> onto <paramref name="target"/> would do
    /// right now — used to preview move/swap/copy in the drag chrome before releasing.</summary>
    public DropOperation PreviewDrop(LoupedeckButton source, LoupedeckButton target, bool copy)
    {
        if (!CanDropOnto(source, target)) return DropOperation.None;
        if (copy) return DropOperation.Copy;
        return HasContent(target) ? DropOperation.Swap : DropOperation.Move;
    }

    /// <summary>
    /// Drag &amp; drop a button / side display onto another of the same kind. Without Ctrl an empty
    /// target is a MOVE (source cleared) and a non-empty target is a SWAP; with Ctrl it is a COPY
    /// that overwrites the target and leaves the source untouched. A Ctrl-copy onto a non-empty
    /// target asks for confirmation first (like Ctrl+V paste); move and swap never lose data and
    /// so are not confirmed.
    /// </summary>
    public async Task DropAsync(LoupedeckButton source, LoupedeckButton target, bool copy)
    {
        if (!CanDropOnto(source, target)) return;
        ButtonKind kind = Classify(source).Value;

        if (kind == ButtonKind.SideDisplay)
        {
            await DropSideDisplay((TouchButton)source, (TouchButton)target, copy);
            SelectButton(target);
            return;
        }

        // Only a Ctrl-copy clobbers the target's configuration; confirm before overwriting a
        // non-empty target. Move (empty target) and swap (non-empty, no Ctrl) lose nothing.
        if (copy && !_clipboard.IsEmpty(target) && !await ConfirmOverwrite()) return;

        TransferOrSwap(source, target, copy, kind);

        switch (kind)
        {
            case ButtonKind.Touch:
                PostTouchChange();
                break;
            case ButtonKind.Simple:
                LoupedeckController.SaveConfig();
                break;
            case ButtonKind.Rotary:
                LoupedeckController.SaveConfig();
                await RefreshRotarySide((RotaryButton)source);
                await RefreshRotarySide((RotaryButton)target);
                break;
        }

        SelectButton(target);
    }

    private void TransferOrSwap(LoupedeckButton source, LoupedeckButton target, bool copy, ButtonKind kind)
    {
        if (copy)
        {
            // Ctrl: overwrite the target, keep the source.
            ButtonSnapshot.Apply(ButtonSnapshot.Capture(source), target);
            return;
        }

        if (_clipboard.IsEmpty(target))
        {
            // Move: target gets the source, the source is emptied.
            ButtonSnapshot.Apply(ButtonSnapshot.Capture(source), target);
            ClearContent(source, kind);
            return;
        }

        // Swap the two configurations.
        string a = ButtonSnapshot.Capture(source);
        string b = ButtonSnapshot.Capture(target);
        ButtonSnapshot.Apply(b, source);
        ButtonSnapshot.Apply(a, target);
    }

    private void ClearContent(LoupedeckButton button, ButtonKind kind)
    {
        switch (kind)
        {
            case ButtonKind.Touch:
            case ButtonKind.SideDisplay:
                ClearTouchContent((TouchButton)button);
                break;
            case ButtonKind.Simple:
                ClearSimpleContent((SimpleButton)button);
                break;
            case ButtonKind.Rotary:
                ClearRotaryContent((RotaryButton)button);
                break;
        }
    }

    private async Task DropSideDisplay(TouchButton source, TouchButton target, bool copy)
    {
        RotarySide srcSide = SideOf(source);
        RotarySide dstSide = SideOf(target);
        if (srcSide == dstSide) return;

        TouchButton srcCanvas = EnsureStripCanvas(srcSide, out RotaryButtonPage srcPage);
        TouchButton dstCanvas = EnsureStripCanvas(dstSide, out RotaryButtonPage dstPage);
        if (srcCanvas == null || dstCanvas == null) return;

        // Confirm a Ctrl-copy that would overwrite a non-empty target strip (see DropAsync).
        if (copy && !_clipboard.IsEmpty(dstCanvas) && !await ConfirmOverwrite()) return;

        if (copy)
        {
            ButtonSnapshot.Apply(ButtonSnapshot.Capture(srcCanvas), dstCanvas);
        }
        else if (_clipboard.IsEmpty(dstCanvas))
        {
            ButtonSnapshot.Apply(ButtonSnapshot.Capture(srcCanvas), dstCanvas);
            ClearTouchContent(srcCanvas);
        }
        else
        {
            string a = ButtonSnapshot.Capture(srcCanvas);
            string b = ButtonSnapshot.Capture(dstCanvas);
            ButtonSnapshot.Apply(b, srcCanvas);
            ButtonSnapshot.Apply(a, dstCanvas);
        }

        LoupedeckController.RegisterStripCanvas(srcPage);
        LoupedeckController.RegisterStripCanvas(dstPage);
        LoupedeckController.SaveConfig();
        await LoupedeckController.RefreshSideStrip(srcSide);
        await LoupedeckController.RefreshSideStrip(dstSide);
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