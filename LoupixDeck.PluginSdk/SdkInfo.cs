namespace LoupixDeck.PluginSdk;

/// <summary>
/// Identifies the contract version of this SDK assembly. A plugin declares the
/// SDK version it was built against in its manifest; the host loads a plugin
/// only when the <see cref="Version.Major"/> components match.
/// </summary>
public static class SdkInfo
{
    /// <summary>The current SDK contract version.</summary>
    public static readonly Version Version = new(1, 11, 0);
}
