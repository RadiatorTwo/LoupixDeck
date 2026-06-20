using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class LoupedeckButton
{
    [ObservableProperty]
    public partial string Command { get; set; }

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

    public void Refresh()
    {
        if (IgnoreRefresh) return;
        ItemChanged?.Invoke(this, EventArgs.Empty);
    }
}