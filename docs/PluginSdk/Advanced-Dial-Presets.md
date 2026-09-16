# Plugin Dial Presets

SDK 1.23.0 lets a plugin offer ready-made rotary configurations next to the
host's built-in and user-created presets. A contributed preset maps any
combination of counter-clockwise rotation, clockwise rotation, and knob press
to commands registered by that plugin.

## `GetDialPresets()`

Override `LoupixPlugin.GetDialPresets()` and return one
`DialPresetDescriptor` for each preset:

```csharp
public override IEnumerable<DialPresetDescriptor> GetDialPresets()
{
    yield return new DialPresetDescriptor
    {
        Id = "level",
        Name = "Level",
        Glyph = "\U000F057E",
        Actions = new Dictionary<RotaryAction, MenuCommandRef>
        {
            [RotaryAction.CounterClockwise] = new()
            {
                CommandName = "MyPlugin.LevelDown",
                Parameters = new Dictionary<string, string> { ["Step"] = "5" }
            },
            [RotaryAction.Clockwise] = new()
            {
                CommandName = "MyPlugin.LevelUp",
                Parameters = new Dictionary<string, string> { ["Step"] = "5" }
            },
            [RotaryAction.Press] = new()
            {
                CommandName = "MyPlugin.Toggle"
            }
        }
    };
}
```

`Id` is stable within the plugin and must not be reused for a different preset.
`Name` is the label users see. `Glyph` is optional; when it is absent, the host
uses its generic preset glyph. `MenuCommandRef.Parameters` uses the same values
as a dynamic-menu command reference and is baked into the assigned command.

An omitted rotary action keeps the dial's existing assignment for that gesture,
so a preset does not need to fill all three slots.

## Host behavior and validation

The host groups contributed presets under the plugin's name. They are read-only:
users can apply them, but cannot edit, rename, or delete them. Applying a preset
copies the commands to the dial once; the dial keeps no reference to the preset,
so later plugin changes do not rewrite existing layouts.

Invalid descriptors are dropped without preventing the plugin from loading. A
preset is rejected when:

- `Id` or `Name` is blank.
- Its `Id` duplicates another preset from the same plugin.
- It contains no usable command.
- A referenced command is not registered or cannot be assigned to a rotary.

The host calls `GetDialPresets()` every time it builds a preset surface. This
allows presets to follow live state, such as one preset per connected endpoint,
and makes devices connected after startup visible without a restart. The call is
synchronous and has no timeout, so return promptly and cache expensive work.

`DialPresetDescriptor` complements `MenuNode.RotaryGroup`. A rotary group is an
entry inside the plugin's own dynamic command tree; a dial preset is a reusable
configuration in the host's preset lists. A plugin may expose both.
