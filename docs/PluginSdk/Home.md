# LoupixDeck.PluginSdk Wiki

`LoupixDeck.PluginSdk` is the contracts-only library that third-party plugins
build against to extend the [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck)
core. It contains no application logic — only the interfaces, base classes and
value types the host and the plugin share. A plugin is a single .NET class
library that references this SDK, ships as a folder under the host's plugin
directory, and is loaded dynamically at startup.

- **Current SDK version:** 1.6.0
- **Target framework:** `net9.0`
- **Package:** `LoupixDeck.PluginSdk` (NuGet)
- **License:** MIT

## Contents

- [Getting Started](Getting-Started) — build your first plugin in ten minutes
- [API Reference](API-Reference) — index of every public type
  - [LoupixPlugin](API-LoupixPlugin) — the plugin entry point
  - [Commands](API-Commands) — `IPluginCommand`, `IDisplayCommand`, descriptors, context
  - [Host Services](API-Host-Services) — `IPluginHost`, logging, settings, device info
- Advanced
  - [Dynamic Menus](Advanced-Menus) — `IMenuContributor`, `MenuNode`
  - [Folder Navigation](Advanced-Folders) — `IFolderProvider`, `FolderEntry`, rotary overrides
  - [Exclusive Mode](Advanced-Exclusive-Mode) — `IExclusiveModeProvider`, `ExclusiveRenderMode`, HUD/screensaver takeovers
  - [Settings Page](Advanced-Settings-Page) — `IPluginSettingsPage`, schema, actions
- Operations
  - [Packaging & Distribution](Packaging-and-Distribution)
  - [Debugging](Debugging)

## Plugin lifecycle at a glance

```
host startup
   │
   ├─ scan plugin folder
   ├─ load each plugin assembly
   ├─ find the single concrete LoupixPlugin subclass
   ├─ check PluginMetadata.SdkVersion.Major matches SdkInfo.Version.Major
   │
   ├─ new MyPlugin()
   ├─ plugin.Initialize(host)            ← host hands over IPluginHost
   ├─ plugin.GetCommands()               ← commands registered
   │
   ├─ … runtime: user invokes commands, host calls IPluginCommand.Execute(ctx)
   │
   └─ on shutdown: plugin.Shutdown()
```

## When you only need to know one thing

Subclass [`LoupixPlugin`](API-LoupixPlugin), return a `PluginMetadata` with
`SdkVersion = SdkInfo.Version`, and yield one or more `IPluginCommand`
implementations from `GetCommands()`. Everything else is optional.
