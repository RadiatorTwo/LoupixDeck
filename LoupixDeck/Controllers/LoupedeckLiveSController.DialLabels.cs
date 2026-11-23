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

    // The dials whose label being replaced or removed (reset, undo, paste) repaints the keys.
    private readonly HashSet<RotaryButton> _wiredDialLabelDials = [];

    private void InitDialLabels()
    {
        if (deviceService.Device is not LoupedeckLiveSDevice device) return;

        device.KeyOverlay = DialLabelOverlayAt;
        _dialLabelResetPage = config.CurrentRotaryButtonPage;
        config.DialLabelSlots = Enumerable.Range(0, device.RotaryCount)
            .Select(device.GetTouchSlotForRotary)
            .Where(slot => slot >= 0)
            .ToArray();
        config.PropertyChanged -= OnDialLabelConfigChanged;
        config.PropertyChanged += OnDialLabelConfigChanged;

        // The first page was drawn before the overlay existed.
        if (config.DialLabelsEnabled)
            Dispatcher.UIThread.Post(RepaintDialLabelKeys);
    }

    /// <summary>Undoes <see cref="InitDialLabels"/> when the device goes away.</summary>
    private void ShutdownDialLabels()
    {
        config.PropertyChanged -= OnDialLabelConfigChanged;
        config.DialLabelSlots = [];
        UnwireDialLabels();
        if (deviceService.Device is { } device)
            device.KeyOverlay = null;
    }

    private void UnwireDialLabels()
    {
        foreach (TouchButton wired in _wiredDialLabels)
            wired.ItemChanged -= DialLabelItemChanged;
        _wiredDialLabels.Clear();

        foreach (RotaryButton dial in _wiredDialLabelDials)
            dial.PropertyChanged -= DialLabelDialChanged;
        _wiredDialLabelDials.Clear();
    }

    // The rotary page whose labels were last reset for being shown, so only a real page change resets.
    private RotaryButtonPage _dialLabelResetPage;

    private void OnDialLabelConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoupedeckConfig.CurrentRotaryButtonPage))
            Dispatcher.UIThread.Post(ResetDialLabelsForPageChange);

        if (e.PropertyName is nameof(LoupedeckConfig.DialLabelsEnabled) or nameof(LoupedeckConfig.DialLabelsKeepKeysClear)
            or nameof(LoupedeckConfig.CurrentRotaryButtonPage))
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

        TouchButton label;
        try
        {
            label = DialLabelForKey(keyIndex);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            // The dial list is edited on the UI thread while this runs on a render thread; the
            // repaint that follows such an edit shows the label again.
            return null;
        }

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

    /// <summary>
    /// Puts the labels of a newly shown rotary page back to their default state when they ask for it
    /// (Reset to default on page change), the same as the touch buttons of a newly shown touch page.
    /// </summary>
    private void ResetDialLabelsForPageChange()
    {
        RotaryButtonPage page = config.CurrentRotaryButtonPage;
        if (ReferenceEquals(page, _dialLabelResetPage)) return;

        bool first = _dialLabelResetPage == null;
        _dialLabelResetPage = page;
        if (first || page?.RotaryButtons == null) return;

        foreach (RotaryButton dial in page.RotaryButtons)
        {
            if (dial?.DialLabel is { ResetOnPageChange: true, Mode: ButtonStateMode.Local } label)
                label.ResetToDefaultState();
        }
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
        UnwireDialLabels();

        var dials = config.CurrentRotaryButtonPage?.RotaryButtons;
        if (dials == null) return;

        foreach (RotaryButton dial in dials)
        {
            if (dial == null) continue;

            dial.PropertyChanged += DialLabelDialChanged;
            _wiredDialLabelDials.Add(dial);

            if (dial.DialLabel is not { } label) continue;

            BitmapHelper.RenderDialLabel(label, DialLabelGeometry.Width(device.KeySize), device.KeySize);
            label.ItemChanged += DialLabelItemChanged;
            _wiredDialLabels.Add(label);
        }
    }

    private void DialLabelItemChanged(object sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RepaintDialLabelKeys);

    private void DialLabelDialChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RotaryButton.DialLabel))
            Dispatcher.UIThread.Post(RepaintDialLabelKeys);
    }

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
}
