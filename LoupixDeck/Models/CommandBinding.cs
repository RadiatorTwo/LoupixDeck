using System.Collections;
using LoupixDeck.Models.Converter;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// The commands bound to one gesture, run in order. Replaces the legacy
/// <c>"a(x) &amp;&amp; b(y)"</c> string. Immutable.
/// </summary>
/// <remarks>
/// Serialized by <see cref="CommandBindingJsonConverter"/>, which still reads the legacy string, so
/// files saved before this type existed load unchanged.
/// </remarks>
[JsonConverter(typeof(CommandBindingJsonConverter))]
public sealed class CommandBinding : IReadOnlyList<CommandCall>, IEquatable<CommandBinding>
{
    private readonly CommandCall[] _calls;

    public static CommandBinding Empty { get; } = new([]);

    public CommandBinding(IEnumerable<CommandCall> calls)
    {
        _calls = (calls ?? []).Where(c => c != null).ToArray();
    }

    /// <summary>
    /// A chain read from the legacy syntax: split at <c>&amp;&amp;</c> exactly as the executor
    /// always did, every segment kept as a text call.
    /// </summary>
    /// <remarks>The chain remembers <paramref name="command"/>, so writing it back unchanged
    /// reproduces the string byte for byte (see <see cref="LegacySource"/>).</remarks>
    public static CommandBinding FromLegacy(string command) =>
        command == null
            ? Empty
            : new CommandBinding(CommandStringParser.SplitChain(command).Select(CommandCall.FromText))
            {
                LegacySource = command
            };

    /// <summary>
    /// A command string in the legacy syntax, read as <see cref="FromLegacy"/> does. Lossless, so
    /// code that builds a command string can assign it directly. Null stays null, so a property
    /// that held null keeps serializing as null.
    /// </summary>
    public static implicit operator CommandBinding(string command) => command == null ? null : FromLegacy(command);

    /// <summary>
    /// The legacy string this chain was read from, or null when it was built some other way. Every
    /// change produces a new chain without it.
    /// </summary>
    public string LegacySource { get; private init; }

    /// <summary>A chain of a single call.</summary>
    public static CommandBinding Of(CommandCall call) => call == null ? Empty : new CommandBinding([call]);

    /// <summary>True for null and for a chain without calls.</summary>
    public static bool IsNullOrEmpty(CommandBinding chain) => chain == null || chain._calls.Length == 0;

    public bool IsEmpty => _calls.Length == 0;

    /// <summary>The first call, or null for an empty chain.</summary>
    public CommandCall First => _calls.Length == 0 ? null : _calls[0];

    /// <summary>True when every call is in the legacy text syntax.</summary>
    public bool IsAllText => _calls.All(c => c.IsText);

    public int Count => _calls.Length;

    public CommandCall this[int index] => _calls[index];

    /// <summary>This chain followed by <paramref name="other"/>.</summary>
    public CommandBinding Append(CommandBinding other) =>
        IsNullOrEmpty(other) ? this : IsEmpty ? other : new CommandBinding(_calls.Concat(other._calls));

    /// <summary>This chain followed by <paramref name="call"/>.</summary>
    public CommandBinding Append(CommandCall call) => call == null ? this : new CommandBinding(_calls.Append(call));

    /// <summary>
    /// The ordered sibling list handed to a display command via
    /// <see cref="CommandContext.SequenceCommands"/>. Empty for a single-command chain, so a
    /// plugin can treat "in a sequence" as a distinct signal; otherwise one entry per call, in
    /// order. The 4-command tile cap is intentionally left to the plugin.
    /// </summary>
    public IReadOnlyList<SequenceCommand> ToSequence() =>
        _calls.Length < 2
            ? []
            : _calls.Select(c => new SequenceCommand(c.Name, c.GetParameters())).ToList();

    /// <summary>The chain in the legacy syntax, for display and for plugins handed a command
    /// string. See <see cref="CommandCall.ToLegacyString"/>.</summary>
    public string ToLegacyString() =>
        LegacySource ?? string.Join(" && ", _calls.Select(c => c.ToLegacyString()));

    public IEnumerator<CommandCall> GetEnumerator() => ((IEnumerable<CommandCall>)_calls).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(CommandBinding other) =>
        other is not null && (ReferenceEquals(this, other) || _calls.AsSpan().SequenceEqual(other._calls));

    public override bool Equals(object obj) => Equals(obj as CommandBinding);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (CommandCall call in _calls)
            hash.Add(call);
        return hash.ToHashCode();
    }

    public override string ToString() => ToLegacyString();
}
