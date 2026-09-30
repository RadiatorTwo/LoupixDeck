using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.Services.Plugins;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>How a row reads at a glance. Drives the status dot and whether the row is dimmed.</summary>
public enum PluginRowStatus
{
    /// <summary>Loaded and running.</summary>
    Ok,

    /// <summary>Enabled, but the change only takes effect on the next start.</summary>
    Restart,

    /// <summary>Present but not running for a reason that is not the user's doing.</summary>
    Neutral,

    /// <summary>Running, but a system requirement the plugin reported is missing (issue #315).</summary>
    Attention,

    /// <summary>Switched off by the user on this device.</summary>
    Disabled
}

/// <summary>One plugin in the installed list.</summary>
public sealed partial class InstalledPluginRowViewModel : ViewModelBase
{
    private readonly Func<InstalledPluginRowViewModel, bool, Task> _setEnabled;
    private bool _suppressToggle;

    public InstalledPluginRowViewModel(LoadedPlugin plugin, bool isEnabled,
        Func<InstalledPluginRowViewModel, bool, Task> setEnabled)
    {
        Plugin = plugin;
        _setEnabled = setEnabled;
        _suppressToggle = true;
        IsEnabled = isEnabled;
        _suppressToggle = false;
    }

    public LoadedPlugin Plugin { get; }

    public string Id => Plugin.Manifest?.Id;

    public string Name => Plugin.Manifest?.Name ?? Plugin.Directory;

    /// <summary>Version on the right of the row, or nothing when the manifest omits it.</summary>
    public string MetaText => string.IsNullOrWhiteSpace(Plugin.Manifest?.Version)
        ? null
        : $"v{Plugin.Manifest.Version}";

    public bool HasMeta => MetaText != null;

    /// <summary>A plugin with an unreadable manifest has no id, so it cannot be toggled.</summary>
    public bool CanToggle => !string.IsNullOrEmpty(Id);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsStatusOk))]
    [NotifyPropertyChangedFor(nameof(IsStatusRestart))]
    [NotifyPropertyChangedFor(nameof(IsStatusAttention))]
    [NotifyPropertyChangedFor(nameof(IsStatusWarn))]
    [NotifyPropertyChangedFor(nameof(IsStatusNeutral))]
    [NotifyPropertyChangedFor(nameof(IsStatusDisabled))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        // The row is also written to when the list rebuilds itself from the config; only a real
        // user toggle may reach the reload coordinator.
        if (_suppressToggle)
            return;

        _ = _setEnabled(this, value);
    }

    /// <summary>Re-reads the status after the plugin's requirements were evaluated again.</summary>
    public void RefreshRequirements()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsStatusOk));
        OnPropertyChanged(nameof(IsStatusAttention));
        OnPropertyChanged(nameof(IsStatusWarn));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatusText));
    }

    /// <summary>Writes the checkbox without running the enable/disable side effect.</summary>
    public void SetEnabledSilently(bool value)
    {
        _suppressToggle = true;
        IsEnabled = value;
        _suppressToggle = false;
    }

    public PluginRowStatus Status
    {
        get
        {
            // Plugins are loaded once for all devices, so a loaded plugin that is off here is
            // running for another device. Disabling is live, so this device simply has it off.
            if (Plugin.Status == PluginLoadStatus.Loaded)
                return !IsEnabled ? PluginRowStatus.Disabled
                    : Plugin.HasUnmetRequirements ? PluginRowStatus.Attention
                    : PluginRowStatus.Ok;

            // Enabled in the config but not running: the load only happens on the next start.
            if (IsEnabled)
                return PluginRowStatus.Restart;

            return Plugin.Status == PluginLoadStatus.Disabled
                ? PluginRowStatus.Disabled
                : PluginRowStatus.Neutral;
        }
    }

    public bool IsStatusOk => Status == PluginRowStatus.Ok;

    public bool IsStatusRestart => Status == PluginRowStatus.Restart;

    public bool IsStatusAttention => Status == PluginRowStatus.Attention;

    /// <summary>Amber dot: something needs the user, whether a restart or a missing requirement.</summary>
    public bool IsStatusWarn => IsStatusRestart || IsStatusAttention;

    public bool IsStatusNeutral => Status == PluginRowStatus.Neutral;

    public bool IsStatusDisabled => Status == PluginRowStatus.Disabled;

    /// <summary>Short wording next to the dot for everything that is not simply running.</summary>
    public string StatusText => Status switch
    {
        PluginRowStatus.Ok => null,
        PluginRowStatus.Restart => Loc.Tr("Plugins_StatusRestart"),
        PluginRowStatus.Attention => Loc.Tr("Plugins_StatusNeedsAttention"),
        PluginRowStatus.Disabled => Loc.Tr("Plugins_StatusDisabled"),
        _ => DescribeStatus(Plugin.Status)
    };

    public bool HasStatusText => StatusText != null;

    /// <summary>The load status in the user's language. The enum names are developer text and
    /// must not reach the window.</summary>
    public static string DescribeStatus(PluginLoadStatus status) => status switch
    {
        PluginLoadStatus.Loaded => Loc.Tr("Plugins_StatusLoaded"),
        PluginLoadStatus.Disabled => Loc.Tr("Plugins_StatusDisabled"),
        PluginLoadStatus.Incompatible => Loc.Tr("Plugins_StatusIncompatible"),
        _ => Loc.Tr("Plugins_StatusFailed")
    };
}
