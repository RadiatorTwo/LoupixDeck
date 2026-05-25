using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// A command as seen by the rest of the app, regardless of whether it originates
/// from a core <c>[Command]</c> class or from a plugin's <c>IPluginCommand</c>.
/// The <see cref="ICommandRegistry"/> unifies both sources into these entries.
/// </summary>
public sealed class RegisteredCommand
{
    /// <summary>Stable command identifier persisted in button assignments.</summary>
    public string CommandName { get; init; }

    /// <summary>UI/command-builder metadata for this command.</summary>
    public CommandInfo Info { get; init; }

    /// <summary>Button types this command may be assigned to.</summary>
    public ButtonTargets SupportedTargets { get; init; } = ButtonTargets.All;

    /// <summary>
    /// When true the command is not listed as a plain leaf in the selection
    /// menu — it is surfaced through a dynamic submenu instead (e.g. one entry
    /// per OBS scene). It stays registered and executable.
    /// </summary>
    public bool HiddenFromMenu { get; init; }

    /// <summary>True when the command renders dynamic text onto a touch button.</summary>
    public bool IsDisplayCommand { get; init; }

    /// <summary>Poll interval for display commands; ignored otherwise.</summary>
    public TimeSpan UpdateInterval { get; init; }

    /// <summary>
    /// Runs the command. The second argument is the button type that triggered
    /// the call — forwarded to <c>CommandContext.Target</c> on plugin commands;
    /// core commands ignore it. The third argument identifies the originating
    /// indexed control (rotary index, slot index) and is forwarded to
    /// <c>CommandContext.SourceIndex</c>; null for chained/CLI invocations.
    /// </summary>
    public Func<string[], ButtonTargets, int?, Task> Execute { get; init; }

    /// <summary>For display commands: produces the current text. Null otherwise.</summary>
    public Func<string[], string> GetText { get; init; }
}
