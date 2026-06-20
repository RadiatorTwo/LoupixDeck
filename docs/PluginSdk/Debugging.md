# Debugging

There is no plugin-side debugger story to invent — a plugin is a .NET class
library loaded into the LoupixDeck host process, so you debug it by attaching
your IDE to the running host. This page covers the practical loop: where to
drop the DLL, how to attach, what to log, and the failure modes you will hit
first.

## Where the host looks for plugins

The host scans a per-user plugin root and loads every plugin folder it finds
inside. The exact path is owned by the LoupixDeck core (not the SDK); the
default conventions are:

| OS | Default plugin root |
|---|---|
| Windows | `%AppData%\LoupixDeck\plugins\` |
| Linux | `$XDG_CONFIG_HOME/LoupixDeck/plugins/` (falls back to `~/.config/LoupixDeck/plugins/`) |
| macOS | `~/Library/Application Support/LoupixDeck/plugins/` |

If your install diverges from these defaults, check the LoupixDeck core
README — the SDK does not encode the path.

Each plugin lives in its own subfolder named after `PluginMetadata.Id`:

```
%AppData%\LoupixDeck\plugins\
└── myplugin\
    ├── MyPlugin.dll
    ├── (runtime deps)
    └── settings.json          ← created by the host on first save
```

## The inner loop

1. **Build** your plugin: `dotnet build` (Debug, not Release — you want
   symbols and unoptimized code).
2. **Stop LoupixDeck** if it is running. Windows holds a file lock on loaded
   DLLs; you cannot overwrite them while the host is up.
3. **Copy** the build output into `<plugin root>/<id>/`. A post-build copy is
   the most reliable way to keep the folder in sync — see snippet below.
4. **Start LoupixDeck**.
5. **Attach** your debugger to the host process:
   - **Rider / Visual Studio:** *Debug → Attach to Process…* → pick the
     `LoupixDeck` process.
   - **VS Code (C# Dev Kit):** *Run → Attach to .NET Process…* → pick the
     host.
6. Set breakpoints in your plugin code. They bind as soon as the host loads
   your assembly (after `Initialize`).

### Post-build copy

Add this to your plugin `.csproj` so every build lands in the host's plugin
folder. Adjust the path to your OS / install.

```xml
<Target Name="CopyToPluginFolder" AfterTargets="Build">
  <PropertyGroup>
    <PluginInstallDir>$(AppData)\LoupixDeck\plugins\$(AssemblyName.ToLower())</PluginInstallDir>
  </PropertyGroup>
  <MakeDir Directories="$(PluginInstallDir)" />
  <ItemGroup>
    <PluginFiles Include="$(OutputPath)**\*.*"
                 Exclude="$(OutputPath)LoupixDeck.PluginSdk.dll;$(OutputPath)LoupixDeck.PluginSdk.xml" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginFiles)"
        DestinationFolder="$(PluginInstallDir)\%(RecursiveDir)"
        SkipUnchangedFiles="true" />
</Target>
```

The `Exclude` is deliberate — never ship the SDK DLL alongside your plugin
([Packaging & Distribution](Packaging-and-Distribution#build-output) explains
why).

### Launch the host from your IDE

To start debugging immediately on F5, set the LoupixDeck executable as the
launch target in your plugin project's `launchSettings.json`:

```json
{
  "profiles": {
    "LoupixDeck (attach)": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\LoupixDeck\\LoupixDeck.exe"
    }
  }
}
```

F5 builds (firing the post-build copy), launches the host, and attaches the
debugger to it in one step.

## Logging

`IPluginLogger` is the only sanctioned way to surface diagnostic output. Every
log line is scoped to your plugin (tagged with `Metadata.Id`) so you can grep
the host log for just your plugin's output.

```csharp
_host.Logger.Info("Connected to backend, polling every 5s");
_host.Logger.Warn($"Slow response: {ms} ms");
_host.Logger.Error("Failed to fetch scenes", ex);
```

**Wrap every `Execute` body in `try/catch`.** Exceptions thrown from `Execute`
are caught by the host but appear as a generic execution failure with no
context. A two-line `try/catch` that calls `Logger.Error("what was being attempted", ex)`
turns a silent failure into an actionable line in the log.

Where the host writes its log file is a core concern — check the LoupixDeck
core README. While debugging, the same lines also appear in the IDE's debug
output (`Console.Error` / `Trace`).

## Inspecting plugin settings

`IPluginSettings` is backed by JSON on disk. While debugging you can open the
file directly:

```
<plugin root>/<id>/settings.json
```

Edit it while the host is stopped to seed test state, then start the host.
Don't edit it while the host is running — the host owns the file and may
overwrite your changes on the next `Save()`.

## Common failure modes

- **The plugin doesn't show up.**
  Check the host log for a load error from your plugin's folder. The usual
  causes are:
  - **SDK major-version mismatch.** `PluginMetadata.SdkVersion.Major` must
    equal the host's SDK major version. Set it to `SdkInfo.Version` and
    rebuild against the matching SDK.
  - **No concrete `LoupixPlugin` subclass.** The host scans your assembly for
    exactly one concrete (non-abstract, public) subclass of `LoupixPlugin`. If
    yours is `internal` or `abstract` it is invisible.
  - **Bundled SDK DLL.** A `LoupixDeck.PluginSdk.dll` alongside your plugin
    fights with the host's own copy. Delete it from the plugin folder.

- **The plugin loads but no commands appear.**
  `GetCommands()` returned empty, or an exception in `Initialize` poisoned the
  plugin. Check the log for an error tagged with your plugin's `Id`.

- **`Execute` does "nothing".**
  Almost always a swallowed exception. Add the `try/catch` + `Logger.Error`
  described above and try again.

- **`File is locked`** during copy.
  LoupixDeck is still running. Stop the host before rebuilding — Windows holds
  a lock on loaded plugin DLLs.

- **Breakpoints never hit.**
  Wrong PDB next to the DLL (copy `.pdb` along with `.dll`), or you attached
  to the wrong process. Confirm the loaded assembly's path in the debugger's
  *Modules* window.
