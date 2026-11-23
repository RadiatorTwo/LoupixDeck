using System.ComponentModel;
using Avalonia.Threading;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Controllers;

/// <summary>
/// Live S dial labels (<see cref="RotaryButton.DialLabel"/>): each dial's label of the current rotary
/// page is drawn over the left edge of the touch key next to that dial. The device composes it onto
/// every write of that key through <see cref="LoupedeckDevice.Device.LoupedeckDevice.KeyOverlay"/>; this
/// part keeps the label bitmaps rendered and repaints the two keys when a label or the dial page changes.
/// </summary>
public partial class LoupedeckLiveSController
{
    // Set while a render of missing label bitmaps is queued, so a burst of key writes queues one.
    private int _dialLabelRenderQueued;

    // The labels whose edits repaint their key; rewired whenever the shown labels change.
    private readonly HashSet<TouchButton> _wiredDialLabels = [];

    private void InitDialLabels()
    {
        if (deviceService.Device is not LoupedeckLiveSDevice device) return;

        device.KeyOverlay = DialLabelOverlayAt;
        config.PropertyChanged -= OnDialLabelConfigChanged;
        config.PropertyChanged += OnDialLabelConfigChanged;
    }

    private void OnDialLabelConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoupedeckConfig.DialLabelsEnabled) or nameof(LoupedeckConfig.CurrentRotaryButtonPage))
            Dispatcher.UIThread.Post(RepaintDialLabelKeys);
    }

    /// <summary>
    /// The overlay of grid key <paramref name="keyIndex"/>: the label of the dial next to it, or null.
    /// Runs under the render gate, so it never renders; a label without a bitmap yet queues the
    /// render and the repaint that shows it.
    /// </summary>
    private SKBitmap DialLabelOverlayAt(int keyIndex)
    {
        if (!config.DialLabelsEnabled || _isDeviceOff || folderNav.IsActive || _screensaverActive ||
            _fullDisplayActive || exclusiveMode.Owns(ExclusiveControlScope.TouchButtons))
            return null;

        TouchButton label = DialLabelForKey(keyIndex);
        if (label == null) return null;

        SKBitmap image = label.RenderedImage;
        if (image == null && Interlocked.Exchange(ref _dialLabelRenderQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _dialLabelRenderQueued, 0);
                RepaintDialLabelKeys();
            });
        }

        return image;
    }

    /// <summary>The label of the current rotary page's dial next to grid key <paramref name="keyIndex"/>.</summary>
    private TouchButton DialLabelForKey(int keyIndex)
    {
        if (deviceService.Device is not LoupedeckLiveSDevice device) return null;

        var dials = config.CurrentRotaryButtonPage?.RotaryButtons;
        if (dials == null) return null;

        for (int dial = 0; dial < dials.Count && dial < device.RotaryCount; dial++)
        {
            if (device.GetTouchSlotForRotary(dial) == keyIndex)
                return dials[dial]?.DialLabel;
        }

        return null;
    }

    /// <summary>
    /// Renders the shown labels and repaints the keys next to the dials, so a changed label, dial
    /// page or setting reaches the device. A key without a label is repainted too: it may have shown
    /// the previous page's label.
    /// </summary>
    public void RepaintDialLabelKeys()
    {
        if (deviceService.Device is not LoupedeckLiveSDevice device) return;

        RenderShownDialLabels(device);

        var keys = config.CurrentTouchButtonPage?.TouchButtons;
        if (keys == null) return;

        for (int dial = 0; dial < device.RotaryCount; dial++)
        {
            int slot = device.GetTouchSlotForRotary(dial);
            TouchButton key = keys.FirstOrDefault(b => b?.Index == slot);
            if (key != null)
                TouchItemChanged(key, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Renders the labels of the current rotary page's dials and listens to their edits. On the UI
    /// thread: publishing a label's bitmap updates the device view.
    /// </summary>
    private void RenderShownDialLabels(LoupedeckLiveSDevice device)
    {
        foreach (TouchButton wired in _wiredDialLabels)
            wired.ItemChanged -= DialLabelItemChanged;
        _wiredDialLabels.Clear();

        var dials = config.CurrentRotaryButtonPage?.RotaryButtons;
        if (dials == null) return;

        foreach (RotaryButton dial in dials)
        {
            if (dial?.DialLabel is not { } label) continue;

            BitmapHelper.RenderDialLabel(label, DialLabelGeometry.Width(device.KeySize), device.KeySize);
            label.ItemChanged += DialLabelItemChanged;
            _wiredDialLabels.Add(label);
        }
    }

    private void DialLabelItemChanged(object sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RepaintDialLabelKeys);

    /// <summary>True when the device has dial labels (the Live S).</summary>
    public bool SupportsDialLabels => deviceService.Device is LoupedeckLiveSDevice;

    /// <summary>Grid key next to dial <paramref name="dial"/>, or -1.</summary>
    public int DialLabelSlot(int dial) =>
        deviceService.Device is LoupedeckLiveSDevice device && dial >= 0 && dial < device.RotaryCount
            ? device.GetTouchSlotForRotary(dial)
            : -1;

    /// <summary>The shown touch page's key that the label of dial <paramref name="dial"/> sits on, or null.</summary>
    public TouchButton KeyUnderDialLabel(int dial)
    {
        int slot = DialLabelSlot(dial);
        return slot < 0 ? null : config.CurrentTouchButtonPage?.TouchButtons?.FirstOrDefault(b => b?.Index == slot);
    }

    /// <summary>True when dial labels are on and a label sits on grid key <paramref name="key"/>.</summary>
    public bool IsUnderDialLabel(TouchButton key)
    {
        if (key == null || !config.DialLabelsEnabled || deviceService.Device is not LoupedeckLiveSDevice device)
            return false;

        for (int dial = 0; dial < device.RotaryCount; dial++)
        {
            if (device.GetTouchSlotForRotary(dial) == key.Index)
                return true;
        }

        return false;
    }
}
