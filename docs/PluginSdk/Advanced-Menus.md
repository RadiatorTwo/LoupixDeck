# Dynamic Menus

Sometimes the list of useful commands isn't known at compile time — OBS scenes,
CoolerControl profiles, currently visible monitors, etc. `IMenuContributor`
lets a plugin contribute submenu entries that are built at the moment the user
opens the command-selection menu.

## IMenuContributor

```csharp
public interface IMenuContributor
{
    Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target);
}
```

Implemented on the same class as your `LoupixPlugin` subclass (or any class
the plugin returns — the host detects the capability via `is IMenuContributor`).
The host calls it when building the menu for a specific button type.

**Implementations must be resilient.** The host applies a timeout and treats
failures as an empty contribution so a slow or offline integration cannot block
the UI. Catch your own exceptions and log them.

## MenuNode

```csharp
public sealed class MenuNode
{
    public required string Name { get; init; }
    public string? CommandName { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<MenuNode> Children { get; init; } = [];
}
```

A node is either:

- A **leaf** — has `CommandName` set (and optional `Parameters`). Selecting it
  builds the command string `CommandName(p1,p2,…)` and assigns it to the
  button.
- A **folder** — has `Children`, no `CommandName`. Selecting it opens a nested
  submenu.

`Parameters` are baked into the persisted command string in the order the
descriptor's `CommandParameter` list declares them.

## Pattern: hide the command from the flat list

Dynamic submenu commands almost always set `HiddenFromMenu = true` on their
descriptor so they don't appear twice (once as a generic leaf, once per
dynamic node).

```csharp
public CommandDescriptor Descriptor { get; } = new()
{
    CommandName = "Obs.SwitchScene",
    DisplayName = "Switch scene",
    Group       = "OBS",
    HiddenFromMenu = true,                         // hidden from the flat list
    ParameterTemplate = "({SceneName})",
    Parameters = [ new CommandParameter("SceneName", typeof(string)) ]
};
```

## Example: one entry per OBS scene

```csharp
public sealed class ObsPlugin : LoupixPlugin, IMenuContributor
{
    private IPluginHost _host = null!;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "obs", Name = "OBS Studio",
        Version = new Version(1, 0, 0), SdkVersion = SdkInfo.Version
    };

    public override void Initialize(IPluginHost host) => _host = host;

    public override IEnumerable<IPluginCommand> GetCommands()
        => [ new SwitchSceneCommand(() => _host) ];

    public async Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        try
        {
            var scenes = await ObsClient.GetScenesAsync();
            return [
                new MenuNode
                {
                    Name = "OBS scenes",
                    Children = scenes.Select(s => new MenuNode
                    {
                        Name = s,
                        CommandName = "Obs.SwitchScene",
                        Parameters = new Dictionary<string, string> { ["SceneName"] = s }
                    }).ToList()
                }
            ];
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"Could not list OBS scenes: {ex.Message}");
            return [];
        }
    }
}
```

The user opens the menu on a touch button → sees **OBS scenes ▸** → expands it
→ picks "Main" → the host persists `Obs.SwitchScene(Main)` to that button.
