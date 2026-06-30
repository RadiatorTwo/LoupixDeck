using System.Collections.ObjectModel;
using System.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// Base for buttons that hold one or more named <see cref="ButtonState"/>s. The active state
/// drives the button's appearance and its press command; a button with a single state behaves
/// exactly like a pre-stateful button. Derived types add their own appearance projections
/// (touch buttons: layers + background; simple buttons: LED color) by overriding
/// <see cref="RaiseActiveStateProjections"/>.
/// </summary>
public abstract class StatefulButton : LoupedeckButton
{
    protected StatefulButton()
    {
        States = new ObservableCollection<ButtonState>();

        // Fresh buttons start with a single default state so they render/execute like before.
        var initial = new ButtonState { Name = "Default" };
        States.Add(initial);
        DefaultStateId = initial.Id;
        ActiveStateId = initial.Id;
        SubscribeActiveState();

        PropertyChanged += OnSelfPropertyChanged;
    }

    private ObservableCollection<ButtonState> _states;

    // Replace (don't append to) the ctor-created default state when deserializing, otherwise
    // each load stacks the JSON states on top of the constructor's default state.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableCollection<ButtonState> States
    {
        get => _states;
        set
        {
            if (ReferenceEquals(_states, value)) return;
            UnsubscribeActiveState();
            _states = value ?? new ObservableCollection<ButtonState>();
            // Re-resolve and re-subscribe the active state against the new collection.
            SubscribeActiveState();
            SyncCommandFromActiveState();
            RaiseActiveStateProjections();
        }
    }

    public Guid DefaultStateId { get; set; }

    public ButtonStateMode Mode { get; set; } = ButtonStateMode.Local;

    /// <summary>Reset to the default state when the owning page becomes active again.</summary>
    public bool ResetOnPageChange { get; set; }

    /// <summary>
    /// Reset to the default state on application restart. When true (default) the active state
    /// is not persisted, so the button always starts on its default state.
    /// </summary>
    public bool ResetOnRestart { get; set; } = true;

    private Guid _activeStateId;

    /// <summary>
    /// The currently active state id. Persisted only when <see cref="ResetOnRestart"/> is false,
    /// so a restart with reset-on-restart lands on <see cref="DefaultStateId"/>.
    /// </summary>
    public Guid ActiveStateId
    {
        get => _activeStateId;
        set => _activeStateId = value;
    }

    /// <summary>Controls whether <see cref="ActiveStateId"/> is written to config.</summary>
    public bool ShouldSerializeActiveStateId() => !ResetOnRestart;

    [JsonIgnore]
    public ButtonState ActiveState
    {
        get
        {
            if (_states == null || _states.Count == 0) return null;
            foreach (var s in _states)
                if (s.Id == _activeStateId) return s;
            return DefaultState ?? _states[0];
        }
    }

    [JsonIgnore]
    public ButtonState DefaultState
    {
        get
        {
            if (_states == null) return null;
            foreach (var s in _states)
                if (s.Id == DefaultStateId) return s;
            return _states.Count > 0 ? _states[0] : null;
        }
    }

    /// <summary>
    /// Switches the active state, re-points the appearance projections, mirrors the new command
    /// and refreshes the button so the device repaints the new state in one pass.
    /// </summary>
    public void SetActiveState(Guid stateId)
    {
        if (_states == null) return;
        if (_activeStateId == stateId && ActiveState != null) return;

        UnsubscribeActiveState();
        _activeStateId = stateId;
        SubscribeActiveState();
        SyncCommandFromActiveState();
        RaiseActiveStateProjections();
        Refresh();
    }

    public void ResetToDefaultState()
    {
        if (DefaultState != null) SetActiveState(DefaultState.Id);
    }

    private ButtonState _subscribedState;

    private void SubscribeActiveState()
    {
        var state = ActiveState;
        if (ReferenceEquals(state, _subscribedState)) return;
        if (_subscribedState != null) _subscribedState.Changed -= ActiveState_Changed;
        _subscribedState = state;
        if (_subscribedState != null) _subscribedState.Changed += ActiveState_Changed;
    }

    private void UnsubscribeActiveState()
    {
        if (_subscribedState != null) _subscribedState.Changed -= ActiveState_Changed;
        _subscribedState = null;
    }

    private void ActiveState_Changed(object sender, EventArgs e) => Refresh();

    private bool _syncingCommand;

    protected void SyncCommandFromActiveState()
    {
        // Mirror the active state's command into the inherited Command so the press handler and
        // editor keep reading button.Command; guard the reentrant write-back below.
        _syncingCommand = true;
        Command = ActiveState?.Command;
        _syncingCommand = false;
    }

    private void OnSelfPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_syncingCommand) return;
        if (e.PropertyName == nameof(Command) && ActiveState != null)
            ActiveState.Command = Command;
    }

    /// <summary>
    /// Raises PropertyChanged for the derived button's active-state projections (e.g. Layers/
    /// BackColor for touch buttons, ButtonColor for simple buttons). Called when the active state
    /// changes or the States collection is replaced.
    /// </summary>
    protected virtual void RaiseActiveStateProjections()
    {
    }

    /// <summary>
    /// Normalizes the active state after JSON deserialization: ActiveStateId is only persisted when
    /// ResetOnRestart is false, so otherwise (or when the stored id no longer resolves) start on the
    /// default state. Re-subscribes the active state and re-mirrors its command. Derived types call
    /// this from their post-load rewire.
    /// </summary>
    protected void NormalizeActiveStateAfterLoad()
    {
        if (ResetOnRestart || ActiveState == null || ActiveState.Id != _activeStateId)
            _activeStateId = DefaultState?.Id ?? (_states is { Count: > 0 } ? _states[0].Id : Guid.Empty);

        SubscribeActiveState();
        SyncCommandFromActiveState();
    }
}
