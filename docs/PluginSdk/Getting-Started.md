# Getting Started

This walkthrough builds a minimal LoupixDeck plugin that contributes a single
command — clicking the assigned button writes a line to the host log.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- An IDE that supports .NET 9 (Rider, Visual Studio 2022 17.12+, or VS Code with
  the C# Dev Kit)
- LoupixDeck installed locally so you can drop the built plugin into its plugin
  folder and watch it load
- A local copy of the `LoupixDeck.PluginSdk` NuGet package (build the SDK
  repository once — it writes the `.nupkg` to `./nupkg/`)

## 1. Create the project

```powershell
dotnet new classlib -n MyPlugin -f net9.0
cd MyPlugin
```

## 2. Reference the SDK

The SDK is consumed as a NuGet package from a local feed. Add a `nuget.config`
next to the `.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="LoupixDeck.PluginSdk" value="..\..\LoupixDeck.PluginSdk\nupkg" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Then reference the package:

```powershell
dotnet add package LoupixDeck.PluginSdk --version 1.2.*
```

> **Important:** Do **not** copy the SDK DLL into your plugin's output folder.
> The host provides the SDK assembly — bundling it causes load conflicts. The
> default NuGet behavior is correct; do not set `<Private>true</Private>`.

## 3. Write the plugin

Replace the generated `Class1.cs` with `MyPlugin.cs`:

```csharp
using LoupixDeck.PluginSdk;

namespace MyPlugin;

public sealed class HelloPlugin : LoupixPlugin
{
    private IPluginHost _host = null!;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "hello",
        Name = "Hello Plugin",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "you",
        Description = "Minimal example plugin."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _host.Logger.Info("Hello plugin loaded.");
    }

    public override IEnumerable<IPluginCommand> GetCommands()
    {
        yield return new SayHelloCommand(() => _host);
    }
}

internal sealed class SayHelloCommand(Func<IPluginHost> hostAccessor) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "Hello.SayHello",
        DisplayName = "Say hello",
        Group = "Hello"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.All;

    public Task Execute(CommandContext ctx)
    {
        hostAccessor().Logger.Info($"Hello from {ctx.Target}!");
        return Task.CompletedTask;
    }
}
```

A few rules baked into this sample:

- Exactly **one** concrete `LoupixPlugin` subclass per assembly. The host picks
  it by reflection and ignores everything else.
- `Metadata.SdkVersion` is always `SdkInfo.Version`. The host refuses to load a
  plugin whose `SdkVersion.Major` doesn't match its own.
- `CommandDescriptor.CommandName` is the **stable identifier** persisted into
  user button configurations. Treat it as a public API — renaming it later
  breaks every config that referenced it.

## 4. Build

```powershell
dotnet build -c Release
```

The output lands in `bin/Release/net9.0/MyPlugin.dll`.

## 5. Install into LoupixDeck

Create a folder named after `Metadata.Id` inside the host's plugin directory
(see [Debugging](Debugging) for the exact path on each OS) and copy your DLL
into it:

```
%AppData%\LoupixDeck\plugins\hello\MyPlugin.dll
```

Start LoupixDeck. Open the command-selection menu on any button — the
**Hello → Say hello** entry should appear. Assign it, press the button, and the
host log shows the line.

## Next steps

- For text that updates on a button (clock, sensor value, scene name) → see
  [`IDisplayCommand`](API-Commands#idisplaycommand).
- For settings UI (API keys, endpoints) → see
  [Settings Page](Advanced-Settings-Page).
- For dynamic menus listing remote state (OBS scenes, sensors) → see
  [Dynamic Menus](Advanced-Menus).
- To attach a debugger and iterate quickly → see [Debugging](Debugging).
