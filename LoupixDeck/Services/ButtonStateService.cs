using LoupixDeck.Models;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.Services;

/// <summary>
/// Lets external callers (plugins, via the host) query and set the active state of stateful
/// touch buttons, addressed by their bound command name — the same addressing scheme as
/// <see cref="LoupixDeck.PluginSdk.IPluginHost.RequestButtonRefresh"/>. A button matches when any
/// of its states' command parses to the given command name, so an External button can be driven
/// regardless of which state is currently active.
/// </summary>
public interface IButtonStateService
{
    /// <summary>State names of the first button bound to <paramref name="commandName"/>, or empty.</summary>
    IReadOnlyList<string> GetStates(string commandName);

    /// <summary>The active state name of the first button bound to <paramref name="commandName"/>, or null.</summary>
    string GetActiveState(string commandName);

    /// <summary>
    /// Sets the active state (by state id or case-insensitive name) of every button bound to
    /// <paramref name="commandName"/>. Returns true if at least one button's state was changed.
    /// The change is marshalled to the UI thread; visible buttons repaint via their refresh hook.
    /// </summary>
    bool SetActiveState(string commandName, string stateNameOrId);
}

public sealed class ButtonStateService(LoupedeckConfig config) : IButtonStateService
{
    public IReadOnlyList<string> GetStates(string commandName)
    {
        var button = FindButtons(commandName).FirstOrDefault();
        if (button?.States == null) return [];
        return button.States.Select(s => s.Name).ToList();
    }

    public string GetActiveState(string commandName)
        => FindButtons(commandName).FirstOrDefault()?.ActiveState?.Name;

    public bool SetActiveState(string commandName, string stateNameOrId)
    {
        if (string.IsNullOrWhiteSpace(stateNameOrId)) return false;

        var changed = false;
        Guid.TryParse(stateNameOrId, out var parsedId);

        foreach (var button in FindButtons(commandName))
        {
            var target = button.States?.FirstOrDefault(s =>
                s.Id == parsedId ||
                string.Equals(s.Name, stateNameOrId, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.Id == button.ActiveStateId) continue;

            var id = target.Id;
            // Apply on the UI thread; the button's ItemChanged hook repaints it when visible.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => button.SetActiveState(id));
            changed = true;
        }

        return changed;
    }

    private IEnumerable<StatefulButton> FindButtons(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            yield break;

        if (config.TouchButtonPages != null)
        {
            foreach (var page in config.TouchButtonPages)
            {
                if (page?.TouchButtons == null) continue;
                foreach (var button in page.TouchButtons)
                {
                    if (button?.States == null) continue;
                    if (button.States.Any(s => StateBindsTo(s, commandName)))
                        yield return button;
                }
            }
        }

        if (config.SimpleButtons != null)
        {
            foreach (var button in config.SimpleButtons)
            {
                if (button?.States == null) continue;
                if (button.States.Any(s => StateBindsTo(s, commandName)))
                    yield return button;
            }
        }
    }

    private static bool StateBindsTo(ButtonState state, string commandName)
    {
        if (string.IsNullOrEmpty(state.Command)) return false;
        foreach (var part in Utils.CommandStringParser.SplitChain(state.Command))
        {
            if (string.Equals(PluginLayerKey.ParseCommandName(part), commandName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
