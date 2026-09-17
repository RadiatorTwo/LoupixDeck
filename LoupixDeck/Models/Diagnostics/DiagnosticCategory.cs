namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// Groups the diagnostic checks in the UI and in the report.
/// Phase 1, the device access of phase 2 and the plugins of phase 3 exist. The installation
/// checks arrive with a later phase and add their own value here.
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>Distribution, kernel, architecture, installation mode, versions.</summary>
    System,

    /// <summary>Desktop environment, session type, XWayland, PipeWire, D-Bus.</summary>
    Session,

    /// <summary>One connected deck: its node, its permissions, its udev rule, its link.</summary>
    DeviceAccess,

    /// <summary>Everything macro playback and the virtual mouse need (/dev/uinput).</summary>
    InputInjection,

    /// <summary>Everything macro recording needs (/dev/input/event*).</summary>
    InputRecording,

    /// <summary>Plugin discovery, manifests and load failures.</summary>
    Plugins
}
