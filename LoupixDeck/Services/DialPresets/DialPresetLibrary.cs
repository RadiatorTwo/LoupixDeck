using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.DialPresets;

/// <summary>
/// The dial presets that ship with the app. Each one binds all three gestures of a dial to a set of
/// commands that belong together, so the common setups need no configuration at all.
/// </summary>
/// <remarks>
/// The table is filtered against the command registry before it is handed out: a preset whose
/// commands are not registered on this platform — the virtual-desktop one on Linux, for instance —
/// is simply not offered, rather than being applied and then doing nothing. The same filter drops a
/// preset whose commands a dial cannot run.
/// </remarks>
public static class DialPresetLibrary
{
    // Fixed ids so a preset keeps its identity across releases.
    private static readonly DialPreset[] All =
    [
        new()
        {
            Id = new Guid("6a1f0b4e-0001-4f5a-9c21-7d3b8e5a1001"),
            Name = "Touch pages",
            Glyph = "\U000F0214", // mdi-file
            Left = "System.PreviousPage",
            Right = "System.NextPage",
            IsBuiltIn = true
        },
        new()
        {
            Id = new Guid("6a1f0b4e-0002-4f5a-9c21-7d3b8e5a1002"),
            Name = "Rotary pages",
            Glyph = "\U000F0493", // mdi-cog
            Left = "System.PreviousRotaryPage",
            Right = "System.NextRotaryPage",
            IsBuiltIn = true
        },
        new()
        {
            Id = new Guid("6a1f0b4e-0003-4f5a-9c21-7d3b8e5a1003"),
            Name = "Workspaces",
            Glyph = "\U000F00D6", // mdi-briefcase
            Left = "System.PreviousWorkspace",
            Right = "System.NextWorkspace",
            Press = "System.GoHomeWorkspace",
            IsBuiltIn = true
        },
        new()
        {
            Id = new Guid("6a1f0b4e-0004-4f5a-9c21-7d3b8e5a1004"),
            Name = "Display brightness",
            Glyph = "\U000F00DF", // mdi-brightness-6
            Left = "System.BrightnessDown",
            Right = "System.BrightnessUp",
            Press = "System.DeviceToggle",
            IsBuiltIn = true
        },
        new()
        {
            Id = new Guid("6a1f0b4e-0005-4f5a-9c21-7d3b8e5a1005"),
            Name = "Mouse wheel",
            Glyph = "\U000F037D", // mdi-mouse
            Left = "System.MouseScroll(-1)",
            Right = "System.MouseScroll(1)",
            Press = "System.MouseClick(Middle)",
            IsBuiltIn = true
        },
        new()
        {
            Id = new Guid("6a1f0b4e-0006-4f5a-9c21-7d3b8e5a1006"),
            Name = "Virtual desktops",
            Glyph = "\U000F0379", // mdi-monitor
            Left = "System.PreviousDesktop",
            Right = "System.NextDesktop",
            IsBuiltIn = true
        }
    ];

    /// <summary>
    /// The built-in presets this installation can actually run: every command they name is
    /// registered, and every one of them is assignable to a rotary encoder.
    /// </summary>
    public static IReadOnlyList<DialPreset> For(ICommandRegistry registry)
    {
        if (registry == null)
            return All;

        return [.. All.Where(preset => IsUsable(preset, registry))];
    }

    /// <summary>True when <paramref name="name"/> is the name of a built-in preset. Checked against
    /// every one of them, not only those this platform can run, so a user preset can never take a
    /// name that would collide on another machine.</summary>
    public static bool IsBuiltInName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        All.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsUsable(DialPreset preset, ICommandRegistry registry) =>
        IsUsable(preset.Left, registry) &&
        IsUsable(preset.Right, registry) &&
        IsUsable(preset.Press, registry);

    private static bool IsUsable(string command, ICommandRegistry registry)
    {
        // An unassigned gesture is fine — a preset need not fill all three.
        if (string.IsNullOrWhiteSpace(command))
            return true;

        foreach (string segment in CommandStringParser.SplitChain(command))
        {
            RegisteredCommand registered = registry.Get(CommandStringParser.GetName(segment));
            if (registered == null || !registered.SupportedTargets.HasFlag(ButtonTargets.RotaryEncoder))
                return false;
        }

        return true;
    }
}
