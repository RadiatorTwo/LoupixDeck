namespace LoupixDeck.PluginSdk;

/// <summary>
/// A plugin command that draws a dynamic touch button onto a host-provided
/// <see cref="IRenderCanvas"/> (90×90), using the host's primitives so its text/symbols stay
/// visually consistent with the core. Set <see cref="IPluginCommand.SupportedTargets"/> to
/// <see cref="ButtonTargets.TouchButton"/>.
/// </summary>
public interface IDisplayImageCommand : IPluginCommand
{
    /// <summary>How often the host should poll <see cref="RenderImage"/>.</summary>
    TimeSpan UpdateInterval { get; }

    /// <summary>
    /// Draws the current button content onto <paramref name="canvas"/>. Return <c>true</c> when
    /// drawn, or <c>false</c> to leave the button unchanged (e.g. no data yet). Must be fast and
    /// synchronous (called on a polling timer); do any network/decoding work asynchronously in
    /// <see cref="LoupixPlugin.Initialize"/> and cache the result.
    /// </summary>
    bool RenderImage(CommandContext ctx, IRenderCanvas canvas);
}
