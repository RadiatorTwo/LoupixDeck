# LoupixPlugin

The abstract base class every plugin subclasses. The host expects **exactly one
concrete `LoupixPlugin` subclass per assembly** — additional subclasses are
ignored.

```csharp
public abstract class LoupixPlugin
{
    public abstract PluginMetadata Metadata { get; }
    public virtual  void Initialize(IPluginHost host) { }
    public virtual  void Shutdown() { }
    public abstract IEnumerable<IPluginCommand> GetCommands();
}
```

## Members

### `Metadata` (abstract)

Identity and versioning. See [PluginMetadata](#pluginmetadata) below. Must be
populated; the host reads it before instantiating any commands.

### `Initialize(IPluginHost host)`

Called once after the plugin is instantiated, **before** `GetCommands()`. Store
the `host` reference — it is the only sanctioned way back into the core. Wire
up background work, open connections, subscribe to external events here.

Exceptions thrown from `Initialize` are caught by the host and logged; the
plugin is then treated as failed and its commands are not registered. Use
`host.Logger.Error(...)` to add context before re-throwing.

### `GetCommands()` (abstract)

Returns all commands the plugin contributes. Called once per load. Yielding
zero commands is legal (e.g. a plugin that only contributes settings or a
folder view).

### `Shutdown()`

Called on plugin unload or application exit. Release resources, cancel timers,
close sockets. Default implementation does nothing.

## Lifecycle ordering

```
ctor → Initialize(host) → GetCommands() → … → Shutdown()
```

The host never calls `Initialize` twice. `Shutdown` runs at most once and may
not run at all if the process is killed.

---

## PluginMetadata

```csharp
public sealed class PluginMetadata
{
    public required string  Id          { get; init; }
    public required string  Name        { get; init; }
    public required Version Version     { get; init; }
    public required Version SdkVersion  { get; init; }
    public string  Author      { get; init; } = string.Empty;
    public string  Description { get; init; } = string.Empty;
    public byte[]? Icon        { get; init; }
}
```

| Member | Notes |
|---|---|
| `Id` | Stable, filesystem-safe identifier (e.g. `obs`). Scopes the plugin's settings directory `plugins/<id>/settings.json`. Do **not** change after the plugin has shipped. |
| `Name` | Human-readable name; also used to alphabetize plugin groups in the command-selection menu. |
| `Version` | The plugin's own version. Bump when you ship. |
| `SdkVersion` | Always assign `SdkInfo.Version`. The host loads the plugin only when the `Major` component matches its own SDK. |
| `Author` | Display only. |
| `Description` | Shown in the plugin manager. |
| `Icon` | Optional raw PNG/SVG bytes. `null` means "no icon"; the host falls back to a generic placeholder. |

## Minimal subclass

```csharp
public sealed class MyPlugin : LoupixPlugin
{
    public override PluginMetadata Metadata { get; } = new()
    {
        Id         = "myplugin",
        Name       = "My Plugin",
        Version    = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version
    };

    public override IEnumerable<IPluginCommand> GetCommands() => [];
}
```
