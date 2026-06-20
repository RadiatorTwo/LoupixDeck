namespace LoupixDeck.PluginSdk;

/// <summary>
/// A node in a dynamically built command submenu (e.g. one OBS scene, one
/// CoolerControl mode, one sensor). A node is either a folder (has
/// <see cref="Children"/>, no <see cref="CommandName"/>) or a leaf that
/// produces a command (has <see cref="CommandName"/>).
/// </summary>
public sealed class MenuNode
{
    /// <summary>Label shown in the menu tree.</summary>
    public required string Name { get; init; }

    /// <summary>Command this leaf builds, or null when the node is a folder.</summary>
    public string? CommandName { get; init; }

    /// <summary>Parameter values to bake into the built command string.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Child nodes when this node is a folder.</summary>
    public IReadOnlyList<MenuNode> Children { get; init; } = [];
}
