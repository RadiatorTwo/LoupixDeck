# Settings Page

`IPluginSettingsPage` lets a plugin declare its user-editable settings
*declaratively* — the host renders the editor, persists the values into the
plugin's `IPluginSettings`, and tells the plugin when something changed. No
UI code in the plugin.

Implement the interface on the same class as your `LoupixPlugin` subclass.

## IPluginSettingsPage

```csharp
public interface IPluginSettingsPage
{
    IReadOnlyList<PluginSettingDescriptor> SettingsSchema  { get; }
    IReadOnlyList<PluginSettingAction>     SettingsActions { get; }
    void OnSettingsSaved();
}
```

| Member | Notes |
|---|---|
| `SettingsSchema` | Editable settings in display order. |
| `SettingsActions` | Optional buttons (e.g. "Test connection"). |
| `OnSettingsSaved()` | Called after the user's edits have been written to `IPluginSettings` and persisted to disk. Reconnect, restart polling, re-validate here — values you cached in fields during `Initialize` are now stale. |

## PluginSettingDescriptor

```csharp
public sealed class PluginSettingDescriptor
{
    public required string             Key          { get; init; }
    public required string             Label        { get; init; }
    public PluginSettingKind           Kind         { get; init; } = PluginSettingKind.Text;
    public string                      Description  { get; init; } = string.Empty;
    public object                      DefaultValue { get; init; }
}
```

One editable field.

- `Key` — storage key in `IPluginSettings`. Read the value back with
  `host.Settings.Get<T>(key)` using the type implied by `Kind` (see below).
- `Label` — field label rendered next to the editor.
- `Kind` — editor kind / stored type. See [PluginSettingKind](#pluginsettingkind).
- `Description` — optional helper text under the field.
- `DefaultValue` — value used when the key is absent. Match the CLR type the
  kind stores.

## PluginSettingKind

```csharp
public enum PluginSettingKind
{
    Text,      // string
    Password,  // string (masked editor)
    Number,    // JSON integer — read with Get<long>
    Toggle     // bool
}
```

| Kind | Editor | Stored as | Read with |
|---|---|---|---|
| `Text` | Single-line text box | `string` | `Get<string>(key)` |
| `Password` | Masked text box | `string` | `Get<string>(key)` |
| `Number` | Numeric editor (integer) | JSON integer | `Get<long>(key)` |
| `Toggle` | On/off switch | `bool` | `Get<bool>(key)` |

## PluginSettingAction

```csharp
public sealed class PluginSettingAction
{
    public required string             Label  { get; init; }
    public required Func<Task<string>> Invoke { get; init; }
}
```

A button shown on the settings form. `Invoke` runs the action and returns a
short status message the host displays inline (e.g. `"Connected"`,
`"Failed: timeout"`). Keep the string short — the host has limited space.

## Example: API token + endpoint + "Test connection"

```csharp
public sealed class MyPlugin : LoupixPlugin, IPluginSettingsPage
{
    private IPluginHost _host = null!;
    private MyClient?   _client;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "myservice", Name = "My Service",
        Version = new Version(1, 0, 0), SdkVersion = SdkInfo.Version
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        RebuildClient();
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];

    // ---- IPluginSettingsPage ----

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = "endpoint", Label = "Endpoint URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = "https://api.example.com"
        },
        new PluginSettingDescriptor
        {
            Key = "token", Label = "API token",
            Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty,
            Description = "Generated in Account → Tokens."
        },
        new PluginSettingDescriptor
        {
            Key = "pollSec", Label = "Poll interval (s)",
            Kind = PluginSettingKind.Number,
            DefaultValue = 5L
        },
        new PluginSettingDescriptor
        {
            Key = "verbose", Label = "Verbose logging",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => [
        new PluginSettingAction
        {
            Label  = "Test connection",
            Invoke = async () =>
            {
                try { return await _client!.PingAsync() ? "Connected" : "No response"; }
                catch (Exception ex) { return $"Failed: {ex.Message}"; }
            }
        }
    ];

    public void OnSettingsSaved() => RebuildClient();

    private void RebuildClient()
    {
        var endpoint = _host.Settings.Get<string>("endpoint")!;
        var token    = _host.Settings.Get<string>("token") ?? string.Empty;
        _client?.Dispose();
        _client = new MyClient(endpoint, token);
    }
}
```
