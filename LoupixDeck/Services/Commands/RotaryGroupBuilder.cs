using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Turns the SDK's rotary-action map — the shape carried by both
/// <see cref="MenuNode.RotaryGroup"/> and <see cref="DialPresetDescriptor.Actions"/> — into the raw
/// command strings the rest of the app assigns to a dial.
/// </summary>
/// <remarks>
/// It goes through the same builder a normal menu leaf goes through, so a command's parameter
/// template and its declared defaults are filled identically however the assignment was reached.
/// Actions whose command does not resolve are dropped rather than written as an empty binding.
/// </remarks>
public static class RotaryGroupBuilder
{
    public static Dictionary<RotaryAction, string> Build(
        IReadOnlyDictionary<RotaryAction, MenuCommandRef> group, ICommandBuilder commandBuilder)
    {
        Dictionary<RotaryAction, string> map = [];

        if (group == null || commandBuilder == null)
            return map;

        foreach ((RotaryAction action, MenuCommandRef reference) in group)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.CommandName))
                continue;

            Dictionary<string, string> parameters = reference.Parameters is { Count: > 0 }
                ? new Dictionary<string, string>(reference.Parameters)
                : null;

            string raw = commandBuilder.CreateCommandFromMenuEntry(
                new MenuEntry(reference.CommandName, reference.CommandName, null, parameters));

            if (!string.IsNullOrWhiteSpace(raw))
                map[action] = raw;
        }

        return map;
    }
}
