using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Commands;

/// <summary>What reconciling a button against its bound command asked for.</summary>
public enum StateSyncResult
{
    /// <summary>Nothing to do — the button's states already match the bound command.</summary>
    Unchanged,

    /// <summary>The command's states were created on the button, which is now command-owned.</summary>
    Materialized,

    /// <summary>
    /// The owning command is gone or was replaced. The caller must ask the user whether the
    /// generated states are kept or discarded and then call
    /// <see cref="ICommandStateMaterializer.Release"/>; nothing has been changed yet.
    /// </summary>
    ReleaseRequested
}

/// <summary>
/// Keeps a stateful button's states in sync with a command that declares its own
/// (<c>CommandDescriptor.States</c>): creates them when such a command is assigned, and releases
/// them again when it is removed or replaced. A command-owned button is driven by its plugin, so
/// it runs in <see cref="ButtonStateMode.External"/> and the editor locks state management —
/// the layers inside each state stay the user's.
/// </summary>
public interface ICommandStateMaterializer
{
    /// <summary>
    /// Compares the button's bound command against its states and applies the harmless cases
    /// (create the declared states, mirror the command into them). Never destroys user content:
    /// a removed or replaced owner is reported as <see cref="StateSyncResult.ReleaseRequested"/>
    /// for the caller to resolve through <see cref="Release"/>.
    /// </summary>
    StateSyncResult Reconcile(StatefulButton button);

    /// <summary>
    /// Hands the states back to the user: <paramref name="keepStates"/> leaves them in place as
    /// ordinary editable states, otherwise everything past the first state is dropped. Either way
    /// the button becomes user-managed and <see cref="ButtonStateMode.Local"/> again.
    /// </summary>
    void Release(StatefulButton button, bool keepStates);

    /// <summary>
    /// The display name of the command that owns <paramref name="button"/>'s states, for the
    /// release prompt. Falls back to the persisted owner key when the command is no longer
    /// registered (its plugin was uninstalled).
    /// </summary>
    string GetOwnerDisplayName(StatefulButton button);
}

public sealed class CommandStateMaterializer(ICommandRegistry commandRegistry) : ICommandStateMaterializer
{
    public StateSyncResult Reconcile(StatefulButton button)
    {
        if (button?.States == null)
            return StateSyncResult.Unchanged;

        RegisteredCommand command = ResolveDeclaringCommand(button.Command);
        string ownerKey = command == null ? null : OwnerKeyOf(button.Command);

        if (!button.HasCommandOwnedStates)
        {
            if (ownerKey == null)
                return StateSyncResult.Unchanged;

            Materialize(button, command, ownerKey);
            return StateSyncResult.Materialized;
        }

        // Still the same owner: only keep the command string (parameters may have been edited)
        // in sync across the generated states.
        if (ownerKey != null && string.Equals(ownerKey, button.StateOwnerCommand, StringComparison.Ordinal))
        {
            MirrorCommandToStates(button);
            return StateSyncResult.Unchanged;
        }

        return StateSyncResult.ReleaseRequested;
    }

    public void Release(StatefulButton button, bool keepStates)
    {
        if (button?.States == null)
            return;

        button.IgnoreRefresh = true;
        try
        {
            if (!keepStates && button.States.Count > 1)
            {
                // The first state carries the layers the user built before the command was
                // assigned, so it is the one that survives.
                ButtonState kept = button.States[0];
                while (button.States.Count > 1)
                    button.States.RemoveAt(1);

                button.DefaultStateId = kept.Id;
                ClearDanglingTransitions(button);
                button.SetActiveState(kept.Id);
            }

            button.StateOwnerCommand = null;
            button.Mode = ButtonStateMode.Local;
        }
        finally
        {
            button.IgnoreRefresh = false;
        }

        button.Refresh();
    }

    public string GetOwnerDisplayName(StatefulButton button)
    {
        if (button == null || !button.HasCommandOwnedStates)
            return string.Empty;

        string name = CommandStringParser.GetName(button.StateOwnerCommand);
        RegisteredCommand command = string.IsNullOrEmpty(name) ? null : commandRegistry.Get(name);
        return command?.Info?.DisplayName ?? button.StateOwnerCommand;
    }

    /// <summary>
    /// Creates the declared states on the button. The state the command was assigned to keeps its
    /// place (and its layers) as the first state; further existing states are reused positionally
    /// so their layers survive too, and surplus states are dropped.
    /// </summary>
    private static void Materialize(StatefulButton button, RegisteredCommand command, string ownerKey)
    {
        List<ButtonState> reusable = [];
        ButtonState owning = button.ActiveState ?? (button.States.Count > 0 ? button.States[0] : null);
        if (owning != null)
            reusable.Add(owning);

        foreach (ButtonState state in button.States)
        {
            if (!ReferenceEquals(state, owning))
                reusable.Add(state);
        }

        button.IgnoreRefresh = true;
        try
        {
            button.States.Clear();

            for (int index = 0; index < command.States.Count; index++)
            {
                ButtonState state = index < reusable.Count ? reusable[index] : new ButtonState();
                state.Name = command.States[index].Name;
                state.Command = button.Command;
                state.Transition.Kind = StateTransitionKind.Stay;
                state.Transition.TargetStateId = null;
                button.States.Add(state);
            }

            button.DefaultStateId = button.States[0].Id;
            button.StateOwnerCommand = ownerKey;
            // The plugin drives the state, so a press must not advance it locally, and a page
            // change must not reset it away from what the plugin reported.
            button.Mode = ButtonStateMode.External;
            button.ResetOnPageChange = false;
            button.SetActiveState(button.States[0].Id);
        }
        finally
        {
            button.IgnoreRefresh = false;
        }

        button.Refresh();
    }

    /// <summary>Keeps every generated state on the button's current command string.</summary>
    private static void MirrorCommandToStates(StatefulButton button)
    {
        foreach (ButtonState state in button.States)
        {
            if (!string.Equals(state.Command, button.Command, StringComparison.Ordinal))
                state.Command = button.Command;
        }
    }

    /// <summary>The command bound to the button, if it declares states; null otherwise.</summary>
    private RegisteredCommand ResolveDeclaringCommand(string boundCommand)
    {
        string name = CommandStringParser.GetName(FirstSegment(boundCommand));
        if (string.IsNullOrEmpty(name))
            return null;

        RegisteredCommand command = commandRegistry.Get(name);
        return command is { DeclaresStates: true } ? command : null;
    }

    /// <summary>
    /// Owner key of the button's command — built from the first segment only, so appending a
    /// second command to the sequence does not look like a different owner.
    /// </summary>
    private static string OwnerKeyOf(string boundCommand) => PluginLayerKey.For(FirstSegment(boundCommand));

    private static string FirstSegment(string boundCommand) =>
        CommandStringParser.SplitChain(boundCommand).FirstOrDefault();

    /// <summary>Drops "jump to a specific state" targets that no longer resolve.</summary>
    private static void ClearDanglingTransitions(StatefulButton button)
    {
        foreach (ButtonState state in button.States)
        {
            if (state.Transition?.Kind != StateTransitionKind.Specific)
                continue;

            Guid? target = state.Transition.TargetStateId;
            if (target == null || button.States.All(candidate => candidate.Id != target.Value))
                state.Transition.TargetStateId = null;
        }
    }
}
