using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class LoupedeckButton
{
    [ObservableProperty]
    public partial string Command { get; set; }

    /// <summary>
    /// UI-only selection highlight shown in the device layout view (single click selects,
    /// the layout draws a border). Transient — never persisted to the config.
    /// </summary>
    [ObservableProperty]
    [JsonIgnore]
    public partial bool IsSelected { get; set; }

    public bool IgnoreRefresh {
        get;
        set;
    }

    /// <summary>
    /// When true, this button's command still runs while the device is in the
    /// OFF state (manual toggle or auto-OFF during system suspend). Used e.g.
    /// for a "wake the device" button that needs to function while everything
    /// else is muted.
    /// </summary>
    public virtual bool EnableWhenOff
    {
        get;
        set => SetProperty(ref field, value);
    }

    public event EventHandler ItemChanged;

    /// <summary>
    /// Raised when live content (a command-drawn image) was swapped in silently. Unlike
    /// <see cref="ItemChanged"/> it does not push to the device or rescan dynamic commands, so an
    /// editor preview can follow it without feeding back into the code that raised it. May fire on
    /// any thread.
    /// </summary>
    public event EventHandler LiveContentChanged;

    public void RaiseLiveContentChanged() => LiveContentChanged?.Invoke(this, EventArgs.Empty);

    public void Refresh()
    {
        if (IgnoreRefresh) return;
        ItemChanged?.Invoke(this, EventArgs.Empty);
    }
}