using System.Collections.ObjectModel;
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
    public SKBitmap Wallpaper =>
        BitmapHelper.GetOrBakeSlot(MainWallpaper, BitmapHelper.PanelWidth, BitmapHelper.PanelHeight);

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
}