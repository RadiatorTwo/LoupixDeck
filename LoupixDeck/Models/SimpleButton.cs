using Avalonia.Media;
using Avalonia.Media.Imaging;
using LoupixDeck.LoupedeckDevice;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// A physical hardware button with an LED. Holds one or more named <see cref="ButtonState"/>s;
/// the LED color and the press command are delegated to the active state (LED-only states carry
/// <see cref="ButtonState.LedColor"/> + a command, no layers). A button with a single state
/// behaves exactly like a pre-stateful button — the v7→v8 migration wraps every old button into
/// one "Default" state.
/// </summary>
public class SimpleButton : StatefulButton
{
    public Constants.ButtonType Id { get; set; }

    protected override void RaiseActiveStateProjections()
    {
        OnPropertyChanged(nameof(ButtonColor));
    }

    /// <summary>The LED color, projected onto the active state's <see cref="ButtonState.LedColor"/>.</summary>
    [JsonIgnore]
    public Color ButtonColor
    {
        get => ActiveState?.LedColor ?? Colors.Black;
        set
        {
            if (ActiveState == null || Equals(ActiveState.LedColor, value)) return;
            ActiveState.LedColor = value; // raises ButtonState.Changed -> Refresh
            OnPropertyChanged(nameof(ButtonColor));
        }
    }

    private Bitmap _renderedImage;

    [JsonIgnore]
    public Bitmap RenderedImage
    {
        get => _renderedImage;
        set
        {
            if (_renderedImage == value) return;
            _renderedImage = value;
            OnPropertyChanged(nameof(RenderedImage));
        }
    }

    /// <summary>
    /// Normalizes the active state and re-mirrors the active command after JSON deserialization.
    /// Call once after config load (simple buttons have no layers to rewire).
    /// </summary>
    public void RewireAfterLoad() => NormalizeActiveStateAfterLoad();
}
