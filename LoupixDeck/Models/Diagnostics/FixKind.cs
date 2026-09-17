namespace LoupixDeck.Models.Diagnostics;

/// <summary>How a <see cref="DiagnosticFix"/> is meant to be applied.</summary>
public enum FixKind
{
    /// <summary>The user has to act; there is no command to run.</summary>
    Manual,

    /// <summary>A single shell command that the user runs.</summary>
    Command,

    /// <summary>The installation script is the source of truth for this repair.</summary>
    InstallerScript
}
