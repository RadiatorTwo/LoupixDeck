# LoupixDeck.PluginSdk

The plugin SDK for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck).

It contains only the contracts shared between the LoupixDeck core and
dynamically loaded integration plugins — no application logic. Integrations
(OBS, Elgato Key Lights, CoolerControl, Argus Monitor, HWiNFO, Windows audio,
and third-party plugins) are built against this SDK and shipped independently
of the core.

## Documentation

SDK documentation is available in the [Wiki](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk/wiki).

## Contracts

| Type | Purpose |
|---|---|
| `LoupixPlugin` | Plugin entry point — one concrete subclass per plugin assembly. |
| `PluginMetadata` | Identity and versioning of a plugin. |
| `IPluginCommand` | A user-assignable action contributed by a plugin. |
| `IDisplayCommand` | A command that additionally renders dynamic text on a touch button. |
| `IMenuContributor` | Optional: contributes dynamically built submenu entries. |
| `IPluginHost` | The bridge from a plugin back into the core. |
| `IPluginSettings` | Per-plugin isolated settings store. |
| `IPluginLogger` | Logging sink scoped to a plugin. |
| `CommandContext` / `CommandDescriptor` | Execution context and declarative command metadata. |
| `ButtonTargets` | Which button types a command supports. |
| `MenuNode` / `DeviceInfo` | Menu and device value types. |
| `SdkInfo` | The SDK contract version. |

## Versioning

The SDK uses SemVer. A plugin declares the SDK version it was built against;
the host loads a plugin only when the **major** version matches.

## Local development

Building this project produces a `.nupkg` under `./nupkg` (via
`GeneratePackageOnBuild`). The LoupixDeck core consumes the SDK as a NuGet
package and resolves it from that folder through a local feed declared in the
core repository's `nuget.config`.

To refresh the package the core builds against:

```bash
dotnet build LoupixDeck.PluginSdk.csproj -c Release
```

Bump `<Version>` in the `.csproj` before building when the contract changes.
