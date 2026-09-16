using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.DialPresets;

/// <summary>
/// Decides whether a dial preset can be offered on this installation: every command it names has to
/// be registered and has to be assignable to a rotary encoder.
/// </summary>
/// <remarks>
/// The same question for the built-in table and for a plugin's contribution, and the answer differs
/// per device — a command the platform does not provide is never registered, and a command a plugin
/// contributes is only registered on the devices that enabled it. A preset that fails the check is
/// not shown, rather than applied and then silently doing nothing.
/// </remarks>
public static class DialPresetCommandFilter
{
    /// <summary>True when every gesture of <paramref name="preset"/> is runnable here.</summary>
    public static bool IsUsable(DialPreset preset, ICommandRegistry registry) =>
        preset != null &&
        IsUsable(preset.Left, registry) &&
        IsUsable(preset.Right, registry) &&
        IsUsable(preset.Press, registry);

    /// <summary>
    /// True when <paramref name="command"/> can be put on a dial. An empty string is fine — a
    /// preset need not fill all three gestures.
    /// </summary>
    public static bool IsUsable(string command, ICommandRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(command))
            return true;

        if (registry == null)
            return false;

        foreach (string segment in CommandStringParser.SplitChain(command))
        {
            RegisteredCommand registered = registry.Get(CommandStringParser.GetName(segment));
            if (registered == null || !registered.SupportedTargets.HasFlag(ButtonTargets.RotaryEncoder))
                return false;
        }

        return true;
    }
}
