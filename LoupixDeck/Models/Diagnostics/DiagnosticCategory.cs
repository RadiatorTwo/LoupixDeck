namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// Groups the diagnostic checks in the UI and in the report.
/// Phase 1 and the device access of phase 2 exist. Plugins, audio and installation checks
/// arrive with the later phases and add their own values here.
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
    InputRecording
}
