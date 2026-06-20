namespace LoupixDeck.PluginSdk;

/// <summary>
/// A plugin command that additionally renders dynamic text onto a touch button.
/// Replaces the core's <c>IDynamicTextProvider</c>. Display commands should set
/// <see cref="IPluginCommand.SupportedTargets"/> to <see cref="ButtonTargets.TouchButton"/>.
/// </summary>
public interface IDisplayCommand : IPluginCommand
{
    /// <summary>How often the host should poll <see cref="GetText"/>.</summary>
    TimeSpan UpdateInterval { get; }

    /// <summary>Produces the current text to display. Must be fast and
    /// synchronous (called on a polling timer).</summary>
    string GetText(CommandContext ctx);
}
