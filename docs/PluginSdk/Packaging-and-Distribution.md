# Packaging & Distribution

A LoupixDeck plugin is a regular .NET class library shipped as a folder
containing the plugin DLL plus any of its runtime dependencies. There is no
manifest file format — the plugin's identity, versioning and SDK compatibility
all come from `PluginMetadata` returned by your `LoupixPlugin` subclass.

## Versioning rules

Two versions matter, and they are different things:

| Field | What it is | Bump when |
|---|---|---|
| `PluginMetadata.Version` | Your plugin's own version | You ship new behavior or bug fixes. |
| `PluginMetadata.SdkVersion` | The SDK contract version you compiled against | Always `SdkInfo.Version` — the SDK package handles it for you. |

The host enforces a **major-version match** on `SdkVersion`. Building against
SDK 1.x runs on any host shipped with SDK 1.x — across the entire 1.x line the
contracts are source- and binary-compatible. A host on SDK 2.x will refuse to
load a plugin built against 1.x (and vice versa).

```csharp
SdkVersion = SdkInfo.Version,   // always this — never hard-code
```

The SDK's `AssemblyVersion` is intentionally pinned at `1.0.0.0` across the
entire 1.x package range so the plugin load context resolves one shared SDK
assembly regardless of which 1.x package the plugin was built against.

## Build output

```powershell
dotnet build -c Release
```

Ship the contents of `bin/Release/net9.0/` **minus** `LoupixDeck.PluginSdk.dll`:

- `MyPlugin.dll` — your plugin
- Any third-party runtime dependencies (HttpClient libs, JSON serializers your
  plugin uses beyond the BCL, etc.)
- Optional `MyPlugin.pdb` if you want symbols available for end-user crash
  reports (the SDK ships embedded PDBs already)

> **Do not redistribute `LoupixDeck.PluginSdk.dll`.** The host provides it.
> Bundling it causes assembly-load conflicts. The NuGet package reference
> already does the right thing — don't manually set `<Private>true</Private>`
> or copy the file by hand.

## Folder layout for distribution

One folder per plugin, named after `Metadata.Id`, placed inside the host's
plugin root:

```
<plugin root>/
└── myplugin/                      ← matches PluginMetadata.Id
    ├── MyPlugin.dll
    ├── ThirdParty.Dep.dll
    └── (your plugin's other runtime deps)
```

Per-plugin data the host creates at runtime sits next to your DLLs:

```
└── myplugin/
    └── settings.json              ← managed by the host (IPluginSettings)
```

See [Debugging](Debugging) for the exact plugin-root path on each OS.

## Plugin icon

`PluginMetadata.Icon` is optional raw bytes (PNG recommended, SVG accepted).
Read the bytes from an embedded resource so you don't depend on the install
layout:

```csharp
public override PluginMetadata Metadata { get; } = new()
{
    Id = "myplugin", Name = "My Plugin",
    Version = new Version(1, 0, 0), SdkVersion = SdkInfo.Version,
    Icon = LoadEmbeddedIcon("MyPlugin.icon.png")
};

private static byte[] LoadEmbeddedIcon(string name)
{
    using var s = typeof(MyPlugin).Assembly.GetManifestResourceStream(name)
                  ?? throw new InvalidOperationException($"Missing resource: {name}");
    using var ms = new MemoryStream();
    s.CopyTo(ms);
    return ms.ToArray();
}
```

In the `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="icon.png" LogicalName="MyPlugin.icon.png" />
</ItemGroup>
```

## Releasing

There is no central plugin registry yet. Typical release flow:

1. Bump `PluginMetadata.Version` and the `<Version>` in the `.csproj`.
2. `dotnet build -c Release`.
3. Zip the `bin/Release/net9.0/` folder (without the SDK DLL).
4. Publish the zip as a GitHub Release on the plugin's own repo and tell users
   to extract it into `<plugin root>/<id>/`.
