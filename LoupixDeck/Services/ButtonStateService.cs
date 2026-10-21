using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;

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

    /// <summary>
    /// Asks <paramref name="commandName"/> (an <c>IButtonStateCommand</c>) which state each button
    /// bound to it should show, with that button's parameters, and shows it. Covers the touch pages
    /// of every workspace, not only the active one, so a workspace switched to later is right too.
    /// Does nothing for a command that does not report states.
    /// </summary>
    void RefreshStates(string commandName);

    /// <summary>
    /// <see cref="RefreshStates"/> for a single button, asking whichever state-reporting command it
    /// is bound to. Used when the button may show a stale state: after its states were created or
    /// its editor closed.
    /// </summary>
    void RefreshState(StatefulButton button);
}

public sealed class ButtonStateService(LoupedeckConfig config, ICommandRegistry commandRegistry = null)
    : IButtonStateService
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

    public void RefreshStates(string commandName)
    {
        RegisteredCommand command = string.IsNullOrWhiteSpace(commandName) ? null : commandRegistry?.Get(commandName);
        if (command?.GetButtonState == null) return;

        foreach (var button in FindButtons(commandName, allWorkspaces: true))
            Refresh(button, command);
    }

    public void RefreshState(StatefulButton button)
    {
        if (button?.States == null || commandRegistry == null) return;

        foreach (var state in button.States)
        {
            if (string.IsNullOrEmpty(state.Command)) continue;
            foreach (var part in Utils.CommandStringParser.SplitChain(state.Command))
            {
                RegisteredCommand command = commandRegistry.Get(PluginLayerKey.ParseCommandName(part));
                if (command?.GetButtonState == null) continue;

                Refresh(button, command);
                return;
            }
        }
    }

    /// <summary>Asks <paramref name="command"/> which state <paramref name="button"/> should show and shows it.</summary>
    private static void Refresh(StatefulButton button, RegisteredCommand command)
    {
        // The parameters come from the segment that binds the button to the command; every state
        // of a command-owned button carries the same command string.
        string segment = button.States
            .Where(s => !string.IsNullOrEmpty(s.Command))
            .SelectMany(s => Utils.CommandStringParser.SplitChain(s.Command))
            .FirstOrDefault(part => string.Equals(PluginLayerKey.ParseCommandName(part), command.CommandName,
                StringComparison.Ordinal));
        if (segment == null) return;

        ButtonTargets kind = button is SimpleButton ? ButtonTargets.SimpleButton : ButtonTargets.TouchButton;
        string wanted = command.GetButtonState(Utils.CommandStringParser.GetParameters(segment), kind,
            button.ActiveState?.Name, button.RuntimeKey);
        if (string.IsNullOrWhiteSpace(wanted)) return;

        var target = button.States.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (target == null || target.Id == button.ActiveStateId) return;

        var id = target.Id;
        // Apply on the UI thread; the button's ItemChanged hook repaints it when visible.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => button.SetActiveState(id));
    }

    private IEnumerable<StatefulButton> FindButtons(string commandName, bool allWorkspaces = false)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            yield break;

        List<Workspace> workspaces = config.ActiveWorkspace != null ? [config.ActiveWorkspace] : [];
        if (allWorkspaces)
        {
            workspaces.AddRange((config.Profiles ?? [])
                .SelectMany(p => p?.Workspaces ?? [])
                .Where(w => w != null && !ReferenceEquals(w, config.ActiveWorkspace)));
        }

        foreach (Workspace workspace in workspaces)
        {
            // Pages and custom folder layouts alike: a folder button bound to a plugin state has
            // to show the right state when the folder is opened.
            foreach (var page in workspace?.EnumerateTouchLayouts() ?? [])
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

        // The LED buttons of every profile, not only the active one: a plugin state change has to
        // reach a button sitting in an inactive profile too, so that profile shows the right state
        // when it is next activated. The touch pages above stay scoped to the active workspace by
        // design — they are repainted from the workspace on every switch anyway.
        foreach (Profile profile in config.Profiles ?? [])
        {
            foreach (SimpleButton button in profile?.SimpleButtons ?? [])
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
