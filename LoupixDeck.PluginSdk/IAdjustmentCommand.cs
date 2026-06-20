namespace LoupixDeck.PluginSdk;

/// <summary>
/// A plugin command that represents a rotary-encoder value adjustment: a
/// continuous parameter the user dials up/down (volume, brightness, zoom),
/// with a press to reset or commit. Set
/// <see cref="IPluginCommand.SupportedTargets"/> to
/// <see cref="ButtonTargets.RotaryEncoder"/>.
///
/// Host-side dispatch for this interface is added alongside the first plugin
/// that needs it; until then plugins should expose three discrete commands
/// (e.g. <c>Foo.Up</c>, <c>Foo.Down</c>, <c>Foo.Reset</c>) bound to the
/// rotary's left/right/press slots, and use <see cref="IPluginCommand.Execute"/>
/// branching on <see cref="CommandContext.Target"/> to route them. Implementing
/// <see cref="IAdjustmentCommand"/> in addition is forward-compatible.
/// </summary>
public interface IAdjustmentCommand : IPluginCommand
{
    /// <summary>Applies a relative change of <paramref name="ticks"/> (positive
    /// for right-turn, negative for left-turn).</summary>
    Task ApplyAdjustment(CommandContext ctx, int ticks);

    /// <summary>Resets the value (typically invoked by an encoder press).</summary>
    Task ApplyReset(CommandContext ctx);

    /// <summary>Current value as display text (e.g. "75%"), or null for no
    /// overlay. Called by the host when rendering a dial indicator.</summary>
    string? GetValueText(CommandContext ctx);
}
