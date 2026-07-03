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

    /// <summary>
    /// True when the command renders a dynamic image (plugin-supplied PNG) onto a touch
    /// button — adapted from an <c>IDisplayImageCommand</c>. The bytes are pushed onto a
    /// plugin-managed <see cref="LoupixDeck.Models.Layers.PluginLayer"/>; an optional
    /// <see cref="GetText"/> is drawn as an overlay. Mutually exclusive with
    /// <see cref="IsDisplayCommand"/> so the text-only path does not also fire.
    /// </summary>
    public bool IsImageDisplayCommand { get; init; }

    /// <summary>
    /// True when the command renders animated frames driven by the central animation scheduler —
    /// adapted from an <c>IAnimatedDisplayCommand</c>. Frames are pushed onto a plugin-managed
    /// <see cref="LoupixDeck.Models.Layers.PluginLayer"/> by the button-animation engine, not the
    /// <see cref="UpdateInterval"/> poll. Mutually exclusive with <see cref="IsDisplayCommand"/> and
    /// <see cref="IsImageDisplayCommand"/> so neither legacy path also fires on this command.
    /// </summary>
    public bool IsAnimatedImageCommand { get; init; }

    /// <summary>For animated image commands: the plugin's desired frame rate (clamped by the host).</summary>
    public int AnimatedTargetFps { get; init; }

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

    /// <summary>
    /// For display commands: produces the current text. Null otherwise. The second argument is the
    /// button's full command sequence (empty for single-command buttons) — forwarded to
    /// <c>CommandContext.SequenceCommands</c> for plugin commands; core commands ignore it.
    /// </summary>
    public Func<string[], IReadOnlyList<SequenceCommand>, string> GetText { get; init; }

    /// <summary>
    /// For image display commands: draws the current button content onto a host canvas, returning
    /// true when drawn (false → leave the button unchanged). Null for non-image commands. The second
    /// argument is the button's full command sequence (empty for single-command buttons).
    /// </summary>
    public Func<string[], IReadOnlyList<SequenceCommand>, LoupixDeck.PluginSdk.IRenderCanvas, bool> RenderImage { get; init; }

    /// <summary>
    /// For animated image commands: draws one animation frame onto a host canvas for the given
    /// timing snapshot, returning whether it drew and whether the animation finished. Null otherwise.
    /// The second argument is the button's full command sequence (empty for single-command buttons).
    /// </summary>
    public Func<string[], IReadOnlyList<SequenceCommand>, LoupixDeck.PluginSdk.IRenderCanvas, AnimationFrameContext, AnimationFrameInfo>
        RenderAnimatedFrame { get; init; }
}
