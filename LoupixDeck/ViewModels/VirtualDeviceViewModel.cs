using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.LoupedeckDevice.Virtual;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>The kinds of control the simulator draws, each with its own shape.</summary>
public enum VirtualControlKind
{
    /// <summary>A rotary encoder: turned with the mouse wheel, pressed with a click.</summary>
    Dial,

    /// <summary>A round LED button, lit in the colour the app sets.</summary>
    Round,

    /// <summary>One of the Loupedeck CT's named square buttons.</summary>
    Named
}

/// <summary>
/// One control of the simulated device: a dial, an LED button or a named button, addressed by the
/// byte the hardware reports it with.
/// </summary>
public sealed partial class VirtualControlViewModel : ObservableObject
{
    private static readonly IBrush DialBrush = new SolidColorBrush(Color.Parse("#FF2A2A2A"));

    public VirtualControlViewModel(VirtualControlKind kind, byte code, string label)
    {
        Kind = kind;
        Code = code;
        Label = label;
        Fill = kind == VirtualControlKind.Round ? Brushes.DimGray : DialBrush;
    }

    public VirtualControlKind Kind { get; }

    public byte Code { get; }

    public string Label { get; }

    public bool IsDial => Kind == VirtualControlKind.Dial;

    public bool IsRound => Kind == VirtualControlKind.Round;

    public bool IsNamed => Kind == VirtualControlKind.Named;

    /// <summary>Face colour; for an LED button the colour last set by the app, grey until then.</summary>
    [ObservableProperty]
    public partial IBrush Fill { get; set; }
}

/// <summary>
/// The simulator window of a virtual device (see <see cref="Utils.VirtualDevice"/>). It shows the
/// framebuffers as the app actually encoded and sent them — decoded back from RGB565 — and turns
/// pointer input into the frames the hardware would send, which enter the device's normal receive
/// path. Nothing here knows about pages, commands or the controller.
/// </summary>
public sealed partial class VirtualDeviceViewModel : ViewModelBase, IDisposable
{
    private const string PanelDisplay = "center";
    private const string WheelDisplay = "knob";

    /// <summary>Touch ids start at 1 and stay below the device's synthetic range (0x80+).</summary>
    private const int MaxTouchId = 0x7F;

    private static readonly Constants.ButtonType[] SideDials =
    [
        Constants.ButtonType.KNOB_TL, Constants.ButtonType.KNOB_CL, Constants.ButtonType.KNOB_BL,
        Constants.ButtonType.KNOB_TR, Constants.ButtonType.KNOB_CR, Constants.ButtonType.KNOB_BR
    ];

    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;
    private readonly VirtualDeviceState _state;
    private readonly Surface _panel;
    private readonly Surface _wheel;
    private readonly Dictionary<byte, VirtualControlViewModel> _leds = new();
    private int _vibrationGeneration;
    private bool _disposed;

    /// <summary>Two bitmaps per display, used alternately: a new reference is what makes the
    /// bound Image repaint, and the one on screen is never written while it is shown.</summary>
    private sealed class Surface(PixelSize size)
    {
        public readonly WriteableBitmap[] Buffers =
        [
            new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque),
            new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque)
        ];

        public int Next;
        public int Pending;
    }

    public VirtualDeviceViewModel(LoupedeckDevice.Device.LoupedeckDevice device, string modelName)
    {
        _device = device;
        _state = device.VirtualState;
        Title = Loc.Tr("VirtualDevice_TitleFmt", modelName);

        IReadOnlyDictionary<string, DisplayInfo> displays = _state.Displays;
        if (displays.TryGetValue(PanelDisplay, out DisplayInfo panel))
        {
            PanelWidth = panel.Width;
            PanelHeight = panel.Height;
            _panel = new Surface(new PixelSize(panel.Width, panel.Height));
        }

        if (displays.TryGetValue(WheelDisplay, out DisplayInfo wheel))
        {
            HasWheel = true;
            WheelSize = wheel.Width;
            _wheel = new Surface(new PixelSize(wheel.Width, wheel.Height));
        }

        BuildControls();

        _state.FrameChanged += OnFrameChanged;
        _state.LedChanged += OnLedChanged;
        _state.BrightnessChanged += OnBrightnessChanged;
        _state.Vibrated += OnVibrated;

        // Whatever was already presented before the window opened.
        PresentFrame(PanelDisplay);
        PresentFrame(WheelDisplay);
        OnBrightnessChanged();
        foreach (byte key in _leds.Keys)
            OnLedChanged(key);
    }

    public string Title { get; }

    public int PanelWidth { get; }

    public int PanelHeight { get; }

    public bool HasWheel { get; }

    public int WheelSize { get; }

    /// <summary>Controls beside the panel's left edge, top to bottom.</summary>
    public ObservableCollection<VirtualControlViewModel> LeftColumn { get; } = [];

    /// <summary>Controls beside the panel's right edge, top to bottom.</summary>
    public ObservableCollection<VirtualControlViewModel> RightColumn { get; } = [];

    /// <summary>Controls in a row below the panel.</summary>
    public ObservableCollection<VirtualControlViewModel> BottomRow { get; } = [];

    public ObservableCollection<VirtualControlViewModel> NamedButtonsLeft { get; } = [];

    public ObservableCollection<VirtualControlViewModel> NamedButtonsRight { get; } = [];

    [ObservableProperty]
    public partial IImage PanelImage { get; set; }

    [ObservableProperty]
    public partial IImage WheelImage { get; set; }

    /// <summary>Brightness as last set by the app, applied to both screens.</summary>
    [ObservableProperty]
    public partial double ScreenOpacity { get; set; } = 1.0;

    /// <summary>True for a moment after the app triggered the haptic motor.</summary>
    [ObservableProperty]
    public partial bool IsVibrating { get; set; }

    // ───────── Input: the frames the hardware would send ─────────

    /// <summary>
    /// A touch on the main panel at panel coordinates; <paramref name="end"/> lifts the finger. On
    /// a device whose grid is physical keys (Razer Stream Controller X) it presses the key under
    /// the pointer instead, as that hardware reports keys, not touches.
    /// </summary>
    public void TouchPanel(int pointerId, double x, double y, bool end)
    {
        if (_device.Geometry.PhysicalKeys)
        {
            PressKeyAt(pointerId, x, y, end);
            return;
        }

        Inject(end ? Constants.Command.TOUCH_END : Constants.Command.TOUCH,
            TouchPayload(pointerId, x, y, PanelWidth, PanelHeight));
    }

    // Physical key held per pointer, so its release goes to the key the press went to.
    private readonly Dictionary<int, byte> _heldKeys = new();

    private void PressKeyAt(int pointerId, double x, double y, bool end)
    {
        if (end)
        {
            if (_heldKeys.Remove(pointerId, out byte held))
                Inject(Constants.Command.BUTTON_PRESS, [held, 0x01]);
            return;
        }

        // A drag keeps the key down; a physical key does not follow the finger.
        if (_heldKeys.ContainsKey(pointerId))
            return;

        int px = Math.Clamp((int)x, 0, Math.Max(0, PanelWidth - 1));
        int py = Math.Clamp((int)y, 0, Math.Max(0, PanelHeight - 1));
        if (_device.PhysicalKeyCode(_device.SlotAt(px, py)) is not { } code)
            return;

        _heldKeys[pointerId] = code;
        Inject(Constants.Command.BUTTON_PRESS, [code, 0x00]);
    }

    /// <summary>A touch on the wheel's own screen at its local coordinates.</summary>
    public void TouchWheel(int pointerId, double x, double y, bool end) =>
        Inject(end ? Constants.Command.WHEEL_TOUCH_END : Constants.Command.WHEEL_TOUCH,
            TouchPayload(pointerId, x, y, WheelSize, WheelSize));

    public void RotateWheel(int delta) => Rotate(CodeOf(Constants.ButtonType.KNOB_CT), delta);

    public void Rotate(VirtualControlViewModel control, int delta) => Rotate(control.Code, delta);

    public void Press(VirtualControlViewModel control, bool down) =>
        Inject(Constants.Command.BUTTON_PRESS, [control.Code, down ? (byte)0x00 : (byte)0x01]);

    private void Rotate(byte code, int delta)
    {
        if (delta == 0)
            return;
        Inject(Constants.Command.KNOB_ROTATE, [code, (byte)(sbyte)Math.Clamp(delta, -127, 127)]);
    }

    private static byte[] TouchPayload(int pointerId, double x, double y, int width, int height)
    {
        int px = Math.Clamp((int)x, 0, Math.Max(0, width - 1));
        int py = Math.Clamp((int)y, 0, Math.Max(0, height - 1));
        byte id = (byte)(1 + (Math.Abs(pointerId) % MaxTouchId));
        return [0, (byte)(px >> 8), (byte)(px & 0xFF), (byte)(py >> 8), (byte)(py & 0xFF), id];
    }

    private void Inject(Constants.Command command, byte[] payload)
    {
        byte[] packet = new byte[3 + payload.Length];
        packet[0] = (byte)packet.Length;
        packet[1] = (byte)command;
        packet[2] = 0; // never a transaction id the app waits on: those start at 1
        payload.CopyTo(packet, 3);
        _device.InjectInput(packet);
    }

    // ───────── Controls ─────────

    /// <summary>
    /// Places the controls the way the device has them, following its main-window layout:
    /// <list type="bullet">
    /// <item>Live S: two dials on the left above LED button 1, LED buttons 2-4 on the right.</item>
    /// <item>Live, Stream Controller, CT: three dials on each side, the LED buttons in a row below;
    /// the CT's named buttons flank its wheel.</item>
    /// <item>Stream Controller X: nothing but the key grid on the panel.</item>
    /// </list>
    /// </summary>
    private void BuildControls()
    {
        List<VirtualControlViewModel> leftDials = [];
        List<VirtualControlViewModel> rightDials = [];
        int sideDials = Math.Clamp(_device.RotaryCount - (HasWheel ? 1 : 0), 0, SideDials.Length);
        foreach (Constants.ButtonType dial in SideDials.Take(sideDials))
        {
            VirtualControlViewModel control = new(VirtualControlKind.Dial, CodeOf(dial), null);
            bool left = dial is Constants.ButtonType.KNOB_TL or Constants.ButtonType.KNOB_CL or Constants.ButtonType.KNOB_BL;
            (left ? leftDials : rightDials).Add(control);
        }

        // Simple button n is ButtonType BUTTON0 + n: the round LED buttons first, then the
        // CT's named square buttons.
        List<VirtualControlViewModel> leds = [];
        foreach (int index in _device.Buttons ?? [])
        {
            Constants.ButtonType type = Constants.ButtonType.BUTTON0 + index;
            if (type <= Constants.ButtonType.BUTTON7)
            {
                VirtualControlViewModel control = new(VirtualControlKind.Round, CodeOf(type), (index + 1).ToString());
                _leds[control.Code] = control;
                leds.Add(control);
            }
            else if (NamedLabel(type) is { } label)
            {
                VirtualControlViewModel control = new(VirtualControlKind.Named, CodeOf(type), label);
                (IsLeftNamed(type) ? NamedButtonsLeft : NamedButtonsRight).Add(control);
            }
        }

        leftDials.ForEach(LeftColumn.Add);
        rightDials.ForEach(RightColumn.Add);

        if (_device is LoupedeckDevice.Device.LoupedeckLiveSDevice)
        {
            // The Live S has no right-hand dials: its first LED button sits under the left
            // dials, the other three form the right-hand column.
            leds.Take(1).ToList().ForEach(LeftColumn.Add);
            leds.Skip(1).ToList().ForEach(RightColumn.Add);
        }
        else
        {
            leds.ForEach(BottomRow.Add);
        }
    }

    private static bool IsLeftNamed(Constants.ButtonType type) => type is
        Constants.ButtonType.CT_HOME or Constants.ButtonType.CT_UNDO or Constants.ButtonType.CT_KEYBOARD or
        Constants.ButtonType.CT_ENTER or Constants.ButtonType.CT_SAVE or Constants.ButtonType.CT_FN_L;

    private static string NamedLabel(Constants.ButtonType type) => type switch
    {
        Constants.ButtonType.CT_HOME => Loc.Tr("VirtualDevice_ButtonHome"),
        Constants.ButtonType.CT_UNDO => Loc.Tr("VirtualDevice_ButtonUndo"),
        Constants.ButtonType.CT_KEYBOARD => Loc.Tr("VirtualDevice_ButtonKeyboard"),
        Constants.ButtonType.CT_ENTER => Loc.Tr("VirtualDevice_ButtonEnter"),
        Constants.ButtonType.CT_SAVE => Loc.Tr("VirtualDevice_ButtonSave"),
        Constants.ButtonType.CT_FN_L or Constants.ButtonType.CT_FN_R => "Fn",
        Constants.ButtonType.CT_A => "A",
        Constants.ButtonType.CT_B => "B",
        Constants.ButtonType.CT_C => "C",
        Constants.ButtonType.CT_D => "D",
        Constants.ButtonType.CT_E => "E",
        _ => null
    };

    private static byte CodeOf(Constants.ButtonType type) =>
        Constants.Buttons.First(kv => kv.Value == type).Key;

    // ───────── Output: what the app sent ─────────

    private void OnFrameChanged(string display)
    {
        Surface surface = SurfaceOf(display);
        if (surface == null)
            return;

        // Coalesce: a burst of refreshes needs only the newest frame on screen.
        if (Interlocked.Exchange(ref surface.Pending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() => PresentFrame(display));
    }

    private void PresentFrame(string display)
    {
        Surface surface = SurfaceOf(display);
        if (surface == null || _disposed)
            return;

        Volatile.Write(ref surface.Pending, 0);
        WriteableBitmap target = surface.Buffers[surface.Next];
        surface.Next ^= 1;

        using (ILockedFramebuffer framebuffer = target.Lock())
            _state.CopyFrame(display, framebuffer.Address, framebuffer.RowBytes);

        if (display == PanelDisplay)
            PanelImage = target;
        else
            WheelImage = target;
    }

    private Surface SurfaceOf(string display) => display switch
    {
        PanelDisplay => _panel,
        WheelDisplay => _wheel,
        _ => null
    };

    private void OnLedChanged(byte key)
    {
        if (!_leds.TryGetValue(key, out VirtualControlViewModel control))
            return;

        Color? color = _state.GetLed(key);
        Dispatcher.UIThread.Post(() =>
            control.Fill = color is { } c ? new SolidColorBrush(c) : Brushes.DimGray);
    }

    private void OnBrightnessChanged()
    {
        // Never fully transparent: a dimmed panel is still a panel, and 0 would look like a crash.
        double opacity = 0.15 + (0.85 * _state.Brightness);
        Dispatcher.UIThread.Post(() => ScreenOpacity = opacity);
    }

    private void OnVibrated()
    {
        int generation = Interlocked.Increment(ref _vibrationGeneration);
        Dispatcher.UIThread.Post(async () =>
        {
            IsVibrating = true;
            await Task.Delay(200);
            if (generation == Volatile.Read(ref _vibrationGeneration))
                IsVibrating = false;
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _state.FrameChanged -= OnFrameChanged;
        _state.LedChanged -= OnLedChanged;
        _state.BrightnessChanged -= OnBrightnessChanged;
        _state.Vibrated -= OnVibrated;

        // Called once the window is gone, so no Image shows these any more.
        foreach (Surface surface in new[] { _panel, _wheel })
        {
            if (surface == null) continue;
            foreach (WriteableBitmap buffer in surface.Buffers)
                buffer.Dispose();
        }
    }
}
