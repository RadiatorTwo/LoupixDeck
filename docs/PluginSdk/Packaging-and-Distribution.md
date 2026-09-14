# Packaging & Distribution

A LoupixDeck plugin is a .NET class library distributed with a `plugin.json`
manifest and any runtime dependencies. LoupixDeck v1.28.0 and later can install
plugins from its curated Plugin Store. Store plugins publish releases from their
own GitHub repository; they are not bundled with the main application.

LoupixDeck v1.28.0 makes no SDK API changes and continues to provide SDK 1.22.0,
so existing plugins need no rebuild merely to use the store.

## Manifest and versioning

Every package needs `plugin.json` at its root. Its identity and versions must
match the `PluginMetadata` returned by the plugin:

```json
{
  "id": "myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.22.0",
  "entryAssembly": "MyPlugin.dll",
  "platform": "All",
  "author": "Example Author",
  "description": "Does one useful thing from the deck.",
  "projectUrl": "https://github.com/example/LoupixDeck.Plugin.MyPlugin",
  "iconFile": "icon.png"
}
```

Required Store-release fields are `id`, `version`, `sdkVersion`, and
`entryAssembly`. `platform` is `All`, `Windows`, or `Linux` and defaults to
`All`. The release workflow requires `version` in plain `major.minor.patch`
form, and a GitHub Release tag must be exactly `v<version>`.

Two versions matter:

| Field | What it is | Bump when |
|---|---|---|
| `version` / `PluginMetadata.Version` | Your plugin's own version | You ship new behavior or bug fixes. |
| `sdkVersion` / `PluginMetadata.SdkVersion` | The SDK contract version you compiled against | You build against a newer SDK contract. |

In code, use the SDK's value instead of hard-coding the contract version:

```csharp
SdkVersion = SdkInfo.Version,
```

The host requires the same SDK major version. The Plugin Store is stricter: it
offers a release only when its SDK version is not newer than the SDK in the
running LoupixDeck and its platform matches the current operating system. Older
compatible releases can therefore remain available to users on an older app.

The SDK's `AssemblyVersion` remains pinned at `1.0.0.0` across the 1.x line so
the plugin load context resolves one shared SDK assembly. The package targets
both `net9.0` and `net10.0`.

## Build output

```powershell
dotnet build -c Release
```

Package the following at the archive root:

- `plugin.json`
- `MyPlugin.dll`, matching `entryAssembly`
- any third-party runtime dependencies
- optional icons or other files the plugin reads at runtime

Do not redistribute `LoupixDeck.PluginSdk.dll`; the host provides it. Bundling
the SDK causes assembly-load conflicts. PDB and `.runtimeconfig.json` files are
not needed by the Store package.

After installation the directory looks like this:

```
<plugin root>/
└── myplugin/
    ├── plugin.json
    ├── MyPlugin.dll
    ├── ThirdParty.Dep.dll
    ├── store.json          ← written by the Plugin Store
    └── settings.json       ← written by IPluginSettings
```

The Store preserves `settings.json` during an update. See [Debugging](Debugging)
for the plugin-root path on each operating system.

## Plugin icon

`PluginMetadata.Icon` is optional raw bytes (PNG recommended, SVG accepted).
Read the bytes from an embedded resource so runtime display does not depend on
the install path:

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

If `plugin.json` also names `iconFile`, include that file separately in the
package for surfaces that read display metadata from the manifest.

## Reusable GitHub release workflow

The Plugin SDK repository provides a reusable workflow that builds and packages
a plugin. Add this caller as `.github/workflows/release.yml` in the plugin's own
repository:

```yaml
name: Plugin Store Release

on:
  release:
    types: [published]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  release:
    uses: RadiatorTwo/LoupixDeck.PluginSdk/.github/workflows/plugin-release.yml@master
```

By default, the workflow builds `<repository-name>.csproj`. If the project is
elsewhere, pass its repository-relative path:

```yaml
    with:
      project: src/MyPlugin.csproj
```

A published GitHub Release produces and attaches:

| Asset | Purpose |
|---|---|
| `<id>-<version>-any.zip` | Package for `platform: All` |
| `<id>-<version>-windows.zip` | Package for `platform: Windows` |
| `<id>-<version>-linux.zip` | Package for `platform: Linux` |
| `plugin.json` | Lets the Store check identity and compatibility before downloading the package |
| `SHA256SUMS` | SHA-256 entries for the package and manifest |

Only the package matching the manifest platform is generated. A manual
`workflow_dispatch` uploads the same files as a workflow artifact but does not
attach them to a GitHub Release. Release notes shown by the Store come from the
GitHub Release description.

## Add the plugin to the Store

Only repositories listed in `plugin-store.json` in the
[LoupixDeck repository](https://github.com/RadiatorTwo/LoupixDeck/blob/master/plugin-store.json)
appear in the Store. After publishing a valid release, open a pull request that
adds an entry such as:

```json
{
  "id": "myplugin",
  "name": "My Plugin",
  "description": "Does one useful thing from the deck.",
  "author": "Example Author",
  "repository": "example/LoupixDeck.Plugin.MyPlugin",
  "icon": "https://example.com/myplugin.png",
  "platforms": ["Windows", "Linux"],
  "minSdkVersion": "1.22.0",
  "commandPrefixes": ["MyPlugin."]
}
```

The catalogue `id` must match the release manifest. `repository` is the GitHub
`owner/name`; `platforms` controls which operating systems see the entry;
`minSdkVersion` is catalogue information while each release's `sdkVersion`
decides actual compatibility. List every stable command prefix in
`commandPrefixes` so LoupixDeck can recognise assignments when the plugin is
not installed and avoid treating them as shell commands.

Before opening the catalogue pull request, verify that the latest stable GitHub
Release contains `plugin.json`, the correctly named ZIP, `SHA256SUMS`, and useful
release notes. Drafts and pre-releases are not offered.

## Manual distribution

Users can still install a compatible ZIP from `Settings > Plugins` or copy its
contents into the user plugin folder. Such a copy is shown as manually installed
and is not updated by the Store. This is useful for development and private
plugins; public Store distribution should use the release workflow above.
