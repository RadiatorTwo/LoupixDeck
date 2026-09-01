# API Reference

The SDK ships a small, stable surface — one base class, a handful of
interfaces, and a few value types. Everything lives in the
`LoupixDeck.PluginSdk` namespace.

## Core (required for every plugin)

| Type | Page | Purpose |
|---|---|---|
| `LoupixPlugin` | [LoupixPlugin](API-LoupixPlugin) | Abstract entry point — exactly one concrete subclass per plugin assembly. |
| `PluginMetadata` | [LoupixPlugin](API-LoupixPlugin#pluginmetadata) | Identity, versioning, author, icon. |
| `IPluginCommand` | [Commands](API-Commands#ipluginCommand) | A single user-assignable action. |
| `CommandDescriptor` | [Commands](API-Commands#commanddescriptor) | Declarative command metadata (name, group, parameters). |
| `CommandContext` | [Commands](API-Commands#commandcontext) | Execution context (parameters, target, device, host). |
| `ButtonTargets` | [Commands](API-Commands#buttontargets) | Flags enum filtering which button types accept the command. |
| `SdkInfo` | here | Static `Version` — always assign to `PluginMetadata.SdkVersion`. |

## Host bridge

| Type | Page | Purpose |
|---|---|---|
| `IPluginHost` | [Host Services](API-Host-Services#ipluginhost) | Bridge handed to the plugin in `Initialize`. |
| `IPluginLogger` | [Host Services](API-Host-Services#ipluginlogger) | Scoped log sink. |
| `IPluginSettings` | [Host Services](API-Host-Services#ipluginsettings) | Per-plugin JSON-backed key/value store. |
| `DeviceInfo` | [Host Services](API-Host-Services#deviceinfo) | Read-only description of the active device. |

## Optional capabilities

| Type | Page | Purpose |
|---|---|---|
| `IDisplayCommand` | [Commands](API-Commands#idisplaycommand) | A command that also renders dynamic text on a touch button. |
| `IMenuContributor` | [Dynamic Menus](Advanced-Menus) | Contributes dynamically built submenu entries. |
| `MenuNode` | [Dynamic Menus](Advanced-Menus#menunode) | Folder or leaf node in a dynamic submenu. |
| `IFolderProvider` | [Folder Navigation](Advanced-Folders#ifolderprovider) | Supplies a folder view on the touch screen. |
| `FolderProviderBase` | [Folder Navigation](Advanced-Folders#folderproviderbase) | Convenience base class. |
| `FolderEntry` | [Folder Navigation](Advanced-Folders#folderentry) | A single grid slot in a folder. |
| `FolderLayout` | [Folder Navigation](Advanced-Folders#folderlayout) | Grid geometry constants. |
| `RotaryOverride` | [Folder Navigation](Advanced-Folders#rotaryoverride) | Per-encoder behavior while a folder is open. |
| `IExclusiveModeProvider` | [Exclusive Mode](Advanced-Exclusive-Mode#iexclusivemodeprovider) | Full-device takeover (HUD, screensaver, video). |
| `ExclusiveRenderMode` | [Exclusive Mode](Advanced-Exclusive-Mode#exclusiverendermode) | How the host pushes a provider's frames, including `None` for provider-owned output. |
| `IFullDisplayRenderer` | [Full-Display Renderer](Advanced-Full-Display-Renderer#ifulldisplayrenderer) | Streams raw BGRA frames to the whole display (video, visualizers). |
| `IFullDisplayRenderSession` | [Full-Display Renderer](Advanced-Full-Display-Renderer#requesting-the-display) | Ownership handle for an active full-display takeover. |
| `FullDisplaySurface` | [Full-Display Renderer](Advanced-Full-Display-Renderer#the-frame-buffer) | Geometry and pixel layout of the frame buffer. |
| `FullDisplayFrameContext` | [Full-Display Renderer](Advanced-Full-Display-Renderer#timing) | Per-frame timing snapshot. |
| `FullDisplayFrameResult` | [Full-Display Renderer](Advanced-Full-Display-Renderer#renderframe-must-be-fast) | What the renderer did this tick (skip / frame / final). |
| `IScreensaverProvider` | [Plugin Rendering](Advanced-Screensavers-and-Side-Strips#plugin-screensavers) | Discoverable full-display renderer factory for the host's idle screensaver. |
| `ISideStripProvider` | [Plugin Rendering](Advanced-Screensavers-and-Side-Strips#side-strip-providers) | Renderer factory that can own one side strip for a rotary page. |
| `ISideStripSession` | [Plugin Rendering](Advanced-Screensavers-and-Side-Strips#side-strip-providers) | One live, disposable strip attachment. |
| `IAnimatedSideStripSession` | [Plugin Rendering](Advanced-Screensavers-and-Side-Strips#animated-side-strip-sessions) | Optional scheduler-driven animation capability for a strip session. |
| `AnimationFrameInfo` | [Plugin Rendering](Advanced-Screensavers-and-Side-Strips#frame-results-and-timing) | Dirty key and skip/frame/final result for an animated strip frame. |
| `IPluginSettingsPage` | [Settings Page](Advanced-Settings-Page#ipluginsettingspage) | Exposes user-editable settings. |
| `PluginSettingDescriptor` | [Settings Page](Advanced-Settings-Page#pluginsettingdescriptor) | One editable setting. |
| `PluginSettingKind` | [Settings Page](Advanced-Settings-Page#pluginsettingkind) | Editor kind enum. |
| `PluginSettingAction` | [Settings Page](Advanced-Settings-Page#pluginsettingaction) | Action button on the settings form. |
| `PluginColor` | [Folder Navigation](Advanced-Folders#plugincolor) | UI-framework-agnostic RGBA color. |

## SdkInfo

```csharp
public static class SdkInfo
{
    public static readonly Version Version = new(1, 20, 0);
}
```

Always set `PluginMetadata.SdkVersion = SdkInfo.Version`. The host loads a
plugin only when `SdkVersion.Major` matches its own — within a major version,
the contracts are guaranteed source- and binary-compatible.

SDK 1.20.0 adds optional screensaver and animated-side-strip contracts plus
`ExclusiveRenderMode.None`. These changes are additive: existing 1.x plugins
continue to load without a rebuild. The package targets both `net9.0` and
`net10.0` so plugins on either target can reference the same contract version.
