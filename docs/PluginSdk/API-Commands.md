# Commands

A *command* is the unit a user assigns to a hardware button, simple key, or
rotary encoder. This page covers the four types that describe and execute a
command: `IPluginCommand`, `IDisplayCommand`, `CommandDescriptor`,
`CommandContext`, and the `ButtonTargets` enum.

## IPluginCommand

```csharp
public interface IPluginCommand
{
    CommandDescriptor Descriptor       { get; }
    ButtonTargets     SupportedTargets { get; }
    Task              Execute(CommandContext ctx);
}
```

A single user-assignable action. The host calls `Execute` on the UI thread's
synchronization context — long work must run asynchronously (return the `Task`,
do not `.Wait()`).

Exceptions thrown by `Execute` are caught and logged by the host but do **not**
unload the plugin. Wrap risky calls in `try/catch` and call
`ctx.Host.Logger.Error(msg, ex)` so the failure is attributed to your command
rather than appearing as a generic host error.

### Example

```csharp
internal sealed class StartRecordingCommand(Func<IPluginHost> host) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "Obs.StartRecording",
        DisplayName = "Start recording",
        Group       = "OBS"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton | ButtonTargets.SimpleButton;

    public async Task Execute(CommandContext ctx)
    {
        try { await ObsClient.StartRecordingAsync(); }
        catch (Exception ex) { ctx.Host.Logger.Error("StartRecording failed", ex); }
    }
}
```

## IDisplayCommand

```csharp
public interface IDisplayCommand : IPluginCommand
{
    TimeSpan UpdateInterval { get; }
    string   GetText(CommandContext ctx);
}
```

Extends `IPluginCommand` with text that is polled and re-rendered on the touch
button. Use it for clocks, sensor readings, current scene names, queue depths,
etc.

- Set `SupportedTargets` to `ButtonTargets.TouchButton` — only touch buttons
  render text.
- `UpdateInterval` is the polling cadence. Pick the longest interval that still
  feels live; 1–5 seconds is usually right.
- `GetText` runs on the polling timer and must be **fast and synchronous**.
  Cache the latest value in a field that your async data path updates, and
  just return that field here.

If your data arrives via push (event subscription) and you want an immediate
redraw without waiting for the next poll, call
`host.RequestButtonRefresh(Descriptor.CommandName)`.

### Example

```csharp
internal sealed class CpuTempCommand(Func<IPluginHost> hostAccessor) : IDisplayCommand
{
    private volatile int _lastTempC;

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "Sensors.CpuTemp",
        DisplayName = "CPU temperature",
        Group       = "Sensors"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;
    public TimeSpan      UpdateInterval   => TimeSpan.FromSeconds(2);

    public string GetText(CommandContext ctx) => $"{_lastTempC}°C";

    public Task Execute(CommandContext ctx) => Task.CompletedTask; // tap = no-op

    public void OnSensorTick(int tempC)
    {
        _lastTempC = tempC;
        hostAccessor().RequestButtonRefresh(Descriptor.CommandName);
    }
}
```

## CommandDescriptor

```csharp
public sealed class CommandDescriptor
{
    public required string  CommandName       { get; init; }
    public required string  DisplayName       { get; init; }
    public required string  Group             { get; init; }
    public string?          ParameterTemplate { get; init; }
    public IReadOnlyList<CommandParameter> Parameters { get; init; } = [];
    public bool             HiddenFromMenu    { get; init; }
}

public sealed class CommandParameter(string name, Type parameterType)
{
    public string Name          { get; }
    public Type   ParameterType { get; }
}
```

| Member | Notes |
|---|---|
| `CommandName` | **Stable identifier** persisted into user button configurations. Treat it as a public API — renaming it later breaks every saved config that referenced it. Convention: `<Plugin>.<Action>`, PascalCase. |
| `DisplayName` | Label in the command-selection menu. |
| `Group` | Submenu/category. Plugins usually use their `Metadata.Name`. |
| `ParameterTemplate` | Placeholder rendered in the command builder, e.g. `({SceneName})`. Null when the command takes no parameters. |
| `Parameters` | Positional parameter definitions in declaration order. Used by the command builder for editors/validation. |
| `HiddenFromMenu` | When `true` the command is not listed as a plain leaf; it surfaces only through dynamic submenus built by [`IMenuContributor`](Advanced-Menus) (e.g. one entry per OBS scene). The command stays fully registered and executable. |

## CommandContext

```csharp
public sealed class CommandContext
{
    public required string[]      Parameters { get; init; }
    public required ButtonTargets Target     { get; init; }
    public DeviceInfo?            Device     { get; init; }
    public required IPluginHost   Host       { get; init; }
}
```

Everything a command needs at execution time.

| Member | Notes |
|---|---|
| `Parameters` | Positional parameter values exactly as parsed from the persisted command string's parentheses. Never `null`; empty when the command takes none. Strings — parse to typed values yourself based on the declared `CommandParameter` types. |
| `Target` | Which button type triggered the command. Useful when a single command should behave differently on a rotary encoder vs. a touch button. |
| `Device` | The active device, or `null` if none. Mirrors `IPluginHost.ActiveDevice`. |
| `Host` | The plugin's `IPluginHost`. Convenient when a command instance is shared across plugins or when you do not want to capture the host in a closure. |

## ButtonTargets

```csharp
[Flags]
public enum ButtonTargets
{
    None          = 0,
    TouchButton   = 1,
    SimpleButton  = 2,
    RotaryEncoder = 4,
    All           = TouchButton | SimpleButton | RotaryEncoder
}
```

Flags enum. `SupportedTargets` filters the command-selection menu of each
button type:

- A user editing a touch button only sees commands whose `SupportedTargets`
  include `TouchButton`.
- `IDisplayCommand` makes no sense on a simple key (no display) — use
  `ButtonTargets.TouchButton`.
- `RotaryEncoder` covers all three rotation/press events; the host invokes
  `Execute` for each, and the command should branch on `ctx.Target` (or on a
  parameter) if it needs to distinguish.
