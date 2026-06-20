namespace LoupixDeck.PluginSdk;

/// <summary>
/// A button the host shows on a plugin's settings form, e.g. "Test Connection".
/// </summary>
public sealed class PluginSettingAction
{
    /// <summary>Button label.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Runs the action and returns a short status message for the host to
    /// display (e.g. "Connected" or "Failed: timeout").
    /// </summary>
    public required Func<Task<string>> Invoke { get; init; }
}
