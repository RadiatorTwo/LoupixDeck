using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Commands;
using LoupixDeck.Commands.Base;
using LoupixDeck.Services;
using LoupixDeck.Utils;

namespace LoupixDeck.ViewModels;

/// <summary>
/// A single editable parameter of a known command. Rendered as a checkbox (bool),
/// a combo box (enum) or a text box (everything else). The value is kept as a
/// string and fed back into <see cref="ICommandBuilder.BuildCommandString"/>,
/// which only ever calls <c>ToString()</c> on it.
/// </summary>
[ObservableObject]
public partial class CommandParameter
{
    public string Name { get; }
    public Type ParameterType { get; }

    public bool IsBool { get; }
    public bool IsEnum { get; }
    public bool IsText => !IsBool && !IsEnum;

    /// <summary>Enum value names for the combo box; null for non-enum parameters.</summary>
    public IReadOnlyList<string> Options { get; }

    public CommandParameter(string name, Type parameterType, string value)
    {
        Name = name;
        ParameterType = parameterType ?? typeof(string);
        IsBool = ParameterType == typeof(bool);
        IsEnum = ParameterType.IsEnum;
        if (IsEnum)
            Options = Enum.GetNames(ParameterType);
        _value = value ?? string.Empty;
    }

    private string _value;
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
        }
    }

    /// <summary>Two-way bridge for the bool checkbox — parses/serialises <see cref="Value"/>.</summary>
    public bool BoolValue
    {
        get => bool.TryParse(_value, out var b) && b;
        set => Value = value ? "True" : "False";
    }
}

/// <summary>
/// One link in a touch button's command chain, shown as a card in the editor.
/// Known commands expose structured <see cref="Parameters"/>; unknown/shell
/// commands fall back to a single free-text field (<see cref="ShellText"/>).
///
/// Note on the first ("Target") parameter: it is rendered as a plain text field
/// (or an enum combo when the type is an enum), not as a populated dropdown of
/// live device/scene values — those are plugin-specific and not retrievable
/// generically without a plugin round-trip. Values containing ',' or ')' are not
/// supported by the executor, so the editor does not support them either.
/// </summary>
[ObservableObject]
public partial class CommandSegment
{
    private readonly ICommandBuilder _commandBuilder;
    private readonly CommandInfo _info;

    /// <summary>Raised whenever the rebuilt command text changes (parameter edit,
    /// shell-text edit). The owning view model recomposes the chained string.</summary>
    public event EventHandler Changed;

    public bool IsKnown => _info != null;
    public string CommandName { get; }
    public string DisplayName { get; }
    public ObservableCollection<CommandParameter> Parameters { get; } = [];

    /// <summary>The current raw text of this single segment (e.g. <c>"OBS.SetScene(Scene 1)"</c>).</summary>
    public string Raw { get; private set; }

    private CommandSegment(ICommandBuilder commandBuilder, CommandInfo info,
        string commandName, string displayName, string raw)
    {
        _commandBuilder = commandBuilder;
        _info = info;
        CommandName = commandName;
        DisplayName = displayName;
        Raw = raw;
    }

    /// <summary>
    /// Builds a segment from a raw command string. <paramref name="info"/> is the
    /// resolved <see cref="CommandInfo"/> when the command name is a known system
    /// command, or null for a shell command.
    /// </summary>
    public static CommandSegment Create(ICommandBuilder commandBuilder, CommandInfo info, string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        var name = CommandStringParser.GetName(raw);

        // Unknown commands and the dedicated Shell command are both edited as free text and
        // stored verbatim as their own segment (like the legacy free-text command field),
        // bypassing the comma-/parenthesis-sensitive parameter editor. A Shell command freshly
        // picked from the menu arrives wrapped as "System.Shell(Shell Command)" — collapse it
        // to an empty free-text card so the user can type the actual command.
        if (info == null || info.CommandName == ShellCommand.CommandName)
        {
            var fromMenu = info != null;
            var shellRaw = fromMenu ? string.Empty : raw;
            var shellDisplay = fromMenu ? info.DisplayName : shellRaw;
            var shell = new CommandSegment(commandBuilder, null,
                CommandStringParser.GetName(shellRaw), shellDisplay, shellRaw)
            {
                _shellText = shellRaw
            };
            return shell;
        }

        var display = string.IsNullOrWhiteSpace(info.DisplayName) ? name : info.DisplayName;
        var segment = new CommandSegment(commandBuilder, info, name, display, raw);

        // Map the positional values parsed from the raw string onto the declared
        // parameters; missing trailing values default to empty.
        var values = CommandStringParser.GetParameters(raw);
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            var descriptor = info.Parameters[i];
            var value = i < values.Length ? values[i] : string.Empty;
            var parameter = new CommandParameter(descriptor.Name, descriptor.ParameterType, value);
            parameter.PropertyChanged += segment.OnParameterChanged;
            segment.Parameters.Add(parameter);
        }

        return segment;
    }

    private void OnParameterChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandParameter.Value)) return;
        RebuildRaw();
    }

    private void RebuildRaw()
    {
        if (!IsKnown) return;

        var values = Parameters.ToDictionary(p => p.Name, object (p) => p.Value);
        Raw = _commandBuilder.BuildCommandString(_info, values);
        OnPropertyChanged(nameof(Raw));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ───────── Shell (unknown command) free-text ─────────

    private string _shellText = string.Empty;
    public string ShellText
    {
        get => _shellText;
        set
        {
            if (_shellText == value) return;
            _shellText = value;
            Raw = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Raw));
            OnPropertyChanged(nameof(Summary));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Compact secondary text for the collapsed card.</summary>
    public string Summary => IsKnown
        ? string.Join(", ", Parameters.Select(p => p.Value).Where(v => !string.IsNullOrWhiteSpace(v)))
        : Raw;

    // ───────── UI state (not persisted) ─────────

    private int _position;

    /// <summary>1-based position in the sequence strip; maintained by the owning
    /// view model whenever the collection changes.</summary>
    public int Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            _position = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFirst));
        }
    }

    /// <summary>True for the first segment — hides the leading "→" connector.</summary>
    public bool IsFirst => _position <= 1;

    private bool _showConnector;

    /// <summary>Whether the leading "→" connector glyph is drawn. Maintained by the
    /// view from layout: false for the very first segment and for any segment that
    /// starts a new wrapped row, so the arrow never dangles at a row's left edge.
    /// The arrow's gutter keeps its width regardless, so toggling this never
    /// re-packs the chips.</summary>
    public bool ShowConnector
    {
        get => _showConnector;
        set { if (_showConnector == value) return; _showConnector = value; OnPropertyChanged(); }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing == value) return; _isEditing = value; OnPropertyChanged(); }
    }

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set { if (_isDragging == value) return; _isDragging = value; OnPropertyChanged(); }
    }
}
