# Host Services

`IPluginHost` is the only sanctioned bridge from a plugin back into the
LoupixDeck core. The host hands an instance to the plugin in `Initialize` and
exposes everything a plugin needs through it.

## IPluginHost

```csharp
public interface IPluginHost
{
    IPluginLogger   Logger        { get; }
    IPluginSettings Settings      { get; }
    DeviceInfo?     ActiveDevice  { get; }

    void RequestButtonRefresh(string commandName);
    void ExecuteCommand(string command);
    void OpenFolder(IFolderProvider provider);
}
```

| Member | Notes |
|---|---|
| `Logger` | Log sink scoped to this plugin — output is tagged with the plugin's `Id`. See [IPluginLogger](#ipluginlogger). |
| `Settings` | Per-plugin JSON-backed key/value store under `plugins/<plugin-id>/settings.json`. See [IPluginSettings](#ipluginsettings). |
| `ActiveDevice` | Currently driven device, or `null` if none. Mirrored on `CommandContext.Device`. |
| `RequestButtonRefresh(commandName)` | Asks the host to re-render every touch button bound to `commandName`. Use after data backing an `IDisplayCommand` changes via push so the user sees the new value immediately instead of at the next poll tick. |
| `ExecuteCommand(command)` | Runs a command string through the host's command pipeline (`Plugin.Command(arg1,arg2)` syntax). Enables chaining across plugin boundaries — e.g. an action that triggers another plugin's command. |
| `OpenFolder(provider)` | Pushes a [folder navigation view](Advanced-Folders) onto the touch screen. The host calls `provider.OnEnter()` and starts rendering from `provider.BuildEntries()`. |

Keep the `IPluginHost` you received in `Initialize` for the plugin's lifetime —
do not try to access host services from a static field or before `Initialize`
runs.

## IPluginLogger

```csharp
public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
```

All log output is scoped to the owning plugin. Use `Error` with the actual
exception object (not just `ex.Message`) so the host can record the full stack
trace.

> **Always log inside `Execute`.** Exceptions thrown from `Execute` are caught
> by the host but appear as a generic execution failure without context. Wrap
> risky calls in `try/catch` and call `Logger.Error("what was being attempted", ex)`
> yourself.

## IPluginSettings

```csharp
public interface IPluginSettings
{
    T?   Get<T>(string key, T? defaultValue = default);
    void Set<T>(string key, T value);
    bool Contains(string key);
    void Remove(string key);
    void Save();
}
```

Isolated per-plugin store backed by `plugins/<plugin-id>/settings.json`.
Plugins never write into the core's `config.json`.

- `Set<T>` updates the in-memory state; nothing is persisted until `Save()` is
  called.
- `Get<T>` returns `defaultValue` when the key is absent or the stored value
  cannot be deserialized to `T`.
- JSON numeric values come back as `long` / `double` — read `PluginSettingKind.Number`
  values with `Get<long>("key")`.

### Example

```csharp
public override void Initialize(IPluginHost host)
{
    _host = host;
    _endpoint = host.Settings.Get<string>("endpoint", "http://localhost:4455")!;
    _pollSec  = host.Settings.Get<long>("pollSec", 5L);
}

private void UpdateEndpoint(string newEndpoint)
{
    _host.Settings.Set("endpoint", newEndpoint);
    _host.Settings.Save();
}
```

For user-editable settings rendered by the host, expose them via
[`IPluginSettingsPage`](Advanced-Settings-Page) — the host then reads and writes
the same `IPluginSettings` keys for you.

## DeviceInfo

```csharp
public sealed record DeviceInfo(string Name, string VendorId, string ProductId, string Slug);
```

Read-only description of the currently driven device. The `Slug` is a stable,
filesystem-safe identifier (use it when caching per-device state). Plugins use
this to adapt to hardware variants without referencing any core type.
