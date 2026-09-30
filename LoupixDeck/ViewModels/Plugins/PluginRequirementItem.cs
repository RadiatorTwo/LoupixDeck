namespace LoupixDeck.ViewModels.Plugins;

/// <summary>One unmet plugin requirement in the detail pane, texts already translated.</summary>
public sealed record PluginRequirementItem(string Name, string Message, string InstallHint)
{
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool HasInstallHint => !string.IsNullOrWhiteSpace(InstallHint);
}
