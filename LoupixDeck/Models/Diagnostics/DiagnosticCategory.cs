namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// Groups the diagnostic checks in the UI and in the report.
/// Only the categories of phase 1 of issue #258 exist. Device access, plugins, audio and
/// installation checks arrive with the later phases and add their own values here.
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>Distribution, kernel, architecture, installation mode, versions.</summary>
    System,

    /// <summary>Desktop environment, session type, XWayland, PipeWire, D-Bus.</summary>
    Session,

    /// <summary>Everything macro playback and the virtual mouse need (/dev/uinput).</summary>
    InputInjection,

    /// <summary>Everything macro recording needs (/dev/input/event*).</summary>
    InputRecording
}
