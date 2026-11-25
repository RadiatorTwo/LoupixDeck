using LoupixDeck.Commands.Base;
using LoupixDeck.Utils;

namespace LoupixDeck.Models;

/// <summary>
/// One command of a <see cref="CommandBinding"/>, in one of two forms.
/// </summary>
/// <remarks>
/// <para><b>Structured</b> (<see cref="Create"/>): a name and the exact parameter values. Nothing
/// is parsed, split or trimmed, so a value may hold commas, parentheses, <c>&amp;&amp;</c>, quotes,
/// line breaks and leading or trailing spaces.</para>
/// <para><b>Text</b> (<see cref="FromText"/>): one segment in the legacy <c>Name(a,b)</c> syntax,
/// kept verbatim and read through <see cref="CommandStringParser"/> exactly as before. It covers
/// commands loaded from a file saved before structured storage existed, shell commands and other
/// free text, commands of a plugin that is not loaded, and strings handed in from outside (CLI,
/// IPC, <c>IPluginHost.ExecuteCommand(string)</c>).</para>
/// </remarks>
public sealed class CommandCall : IEquatable<CommandCall>
{
    private readonly string[] _parameters;

    private CommandCall(string name, string[] parameters, string text)
    {
        Name = name;
        _parameters = parameters;
        Text = text;
    }

    /// <summary>A structured call. Null parameter values become empty strings; nothing is trimmed.</summary>
    public static CommandCall Create(string name, IEnumerable<string> parameters = null) =>
        new(name ?? string.Empty, (parameters ?? []).Select(p => p ?? string.Empty).ToArray(), null);

    /// <summary>A call in the legacy text syntax, kept verbatim.</summary>
    /// <param name="segment">One segment of a chain, without <c>&amp;&amp;</c>.</param>
    public static CommandCall FromText(string segment)
    {
        string text = segment ?? string.Empty;
        return new CommandCall(CommandStringParser.GetName(text), null, text);
    }

    /// <summary>The command name (for a text call, everything before its <c>'('</c>).</summary>
    public string Name { get; }

    /// <summary>The legacy text of a text call; null for a structured one.</summary>
    public string Text { get; }

    /// <summary>True for a call in the legacy text syntax.</summary>
    public bool IsText => Text != null;

    /// <summary>
    /// The values the command receives. A structured call returns its values exactly. A text call
    /// parses them the way the executor always has, honouring
    /// <see cref="CommandInfo.LastParameterTakesRest"/> when <paramref name="info"/> is given.
    /// </summary>
    public string[] GetParameters(CommandInfo info = null)
    {
        if (!IsText)
            return (string[])_parameters.Clone();

        return info == null
            ? CommandStringParser.GetParameters(Text)
            : CommandStringParser.GetParameters(Text, info);
    }

    /// <summary>
    /// The call in the legacy syntax: verbatim for a text call, <c>Name(a,b)</c> for a structured
    /// one. Lossy for structured values that contain the syntax's own characters, so it is only
    /// for display and for plugins that are handed a command string.
    /// </summary>
    public string ToLegacyString() =>
        IsText ? Text
        : _parameters.Length == 0 ? Name
        : $"{Name}({string.Join(",", _parameters)})";

    /// <summary>
    /// Two structured calls are equal when name and values match exactly. When either is a text
    /// call, both are compared the way the executor reads them: name plus the parsed values.
    /// </summary>
    public bool Equals(CommandCall other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal)) return false;

        return !IsText && !other.IsText
            ? _parameters.AsSpan().SequenceEqual(other._parameters)
            : GetParameters().AsSpan().SequenceEqual(other.GetParameters());
    }

    public override bool Equals(object obj) => Equals(obj as CommandCall);

    // Only the name: text and structured calls can be equal, and their values are read differently.
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => ToLegacyString();
}
