using System.Collections.ObjectModel;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public sealed partial class TouchButtonPage(int pageSize) : ButtonPageBase()
{
    public ObservableCollection<TouchButton> TouchButtons { get; } = new(Enumerable.Range(0, pageSize).Select(static i => new TouchButton(i)));

    /// <summary>
    /// Main 480×270 wallpaper. Always non-null; an empty slot (no
    /// <see cref="WallpaperSlot.AssetPath"/>) means "no wallpaper".
    /// </summary>
    public WallpaperSlot MainWallpaper
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.Changed -= OnWallpaperSlotChanged;
            field = value;
            field?.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    } = new();

    /// <summary>
    /// Optional wallpaper for the left Razer side display (60×270). When set it
    /// overdraws the main wallpaper's left region; empty falls back to the main.
    /// </summary>
    public WallpaperSlot LeftWallpaper
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.Changed -= OnWallpaperSlotChanged;
            field = value;
            field?.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    } = new();

    /// <summary>Optional wallpaper for the right Razer side display (60×270).</summary>
    public WallpaperSlot RightWallpaper
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.Changed -= OnWallpaperSlotChanged;
            field = value;
            field?.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    } = new();

    /// <summary>
    /// Baked main wallpaper, used for thumbnails/previews (settings list). Read-only;
    /// computed on demand from <see cref="MainWallpaper"/>. Returns null when unset.
    /// </summary>
    [JsonIgnore]
    public SKBitmap Wallpaper => BitmapHelper.GetOrBakeSlot(MainWallpaper, Geometry.PanelWidth, Geometry.PanelHeight);

    /// <summary>
    /// Panel geometry of the owning device, used to bake the preview above at the same size
    /// the renderer uses (a mismatch would bake a second copy into the slot cache).
    /// Runtime-only; assigned by <see cref="LoupedeckConfig.ApplyDeviceGeometry"/> on load and
    /// by the page manager for pages added later.
    /// </summary>
    [JsonIgnore]
    public DeviceGeometry Geometry { get; set; } = DeviceGeometry.Default;

    /// <summary>Change signal (no value) raised whenever any wallpaper slot's
    /// rendered result changes, so the controller repaints. JsonIgnore — purely a
    /// notification, never persisted.</summary>
    [JsonIgnore]
    public bool WallpaperInvalidated => false;

    private void OnWallpaperSlotChanged(object sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Wallpaper));
        OnPropertyChanged(nameof(WallpaperInvalidated));
    }

    /// <summary>Pre/Post-command wrap applied to every touch button on this page.</summary>
    public CommandWrap TouchButtonWrap { get; set; } = new();

    /// <summary>
    /// The Loupedeck CT's centre wheel modes for this page. Each mode is a full wheel binding —
    /// rotate left/right, press (a tap on the wheel's screen) and the label drawn on that screen —
    /// and the user switches between them from the wheel's on-screen menu (swipe up/down, tap).
    /// Kept on the touch page rather than in a rotary page set so the wheel follows the page,
    /// workspace and profile the user is on. Never empty; consumed only on devices with a wheel.
    /// Replace, not reuse: Newtonsoft would otherwise append the saved modes to the default one.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<RotaryButton> WheelModes
    {
        get;
        set
        {
            field = value is { Count: > 0 } ? value : [NewWheelMode()];
            OnPropertyChanged();
            OnPropertyChanged(nameof(Wheel));
            OnPropertyChanged(nameof(WheelModeLabel));
        }
    } = [NewWheelMode()];

    /// <summary>Which of <see cref="WheelModes"/> is active. Persisted, so each page reopens on the
    /// mode it was left on; out-of-range values (a hand-edited config) clamp.</summary>
    public int WheelModeIndex
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Wheel));
            OnPropertyChanged(nameof(WheelModeLabel));
        }
    }

    /// <summary>The active wheel mode — what turning and pressing the wheel run and what its screen
    /// shows.</summary>
    [JsonIgnore]
    public RotaryButton Wheel => WheelModes[Math.Clamp(WheelModeIndex, 0, WheelModes.Count - 1)];

    /// <summary>"active / total" for the device view's wheel-mode pager.</summary>
    [JsonIgnore]
    public string WheelModeLabel => $"{Math.Clamp(WheelModeIndex, 0, WheelModes.Count - 1) + 1} / {WheelModes.Count}";

    /// <summary>
    /// Reads the single-binding <c>Wheel</c> that pages carried before wheel modes existed and
    /// keeps it as the only mode. Read-only for JSON: it is never written back.
    /// </summary>
    [JsonProperty("Wheel")]
    private RotaryButton LegacyWheel
    {
        get => null;
        set
        {
            if (value != null) WheelModes = [value];
        }
    }

    public bool ShouldSerializeLegacyWheel() => false;

    /// <summary>Notifies the device view after <see cref="WheelModes"/> was changed in place.</summary>
    public void NotifyWheelModesChanged()
    {
        OnPropertyChanged(nameof(Wheel));
        OnPropertyChanged(nameof(WheelModeLabel));
    }

    public static RotaryButton NewWheelMode() =>
        new(LoupedeckDevice.Device.LoupedeckDevice.WheelRotaryIndex, string.Empty, string.Empty);
}