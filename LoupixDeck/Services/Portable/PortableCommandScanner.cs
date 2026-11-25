using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Finds the commands inside a serialized config subtree (or macro document) and dissects them into
/// command names and macro references. Both stored forms are read: the legacy string and the array
/// of structured calls. Used by the profile package exporter to work out
/// which plugins and macros a package must carry, and by the importer to report what an incoming
/// package references.
/// </summary>
/// <remarks>
/// Unlike <see cref="AssetPathHarvester"/>, this scan is property-name driven rather than
/// value-shaped: an asset path is recognizable on sight ("assets/…"), a command string is not —
/// a schema-agnostic "every string that contains a dot" sweep would drag page names, labels and
/// file names into a list the user is shown. The property names below are the complete set of
/// command-bearing fields in the config model; extend it when a new one is added.
/// </remarks>
public static class PortableCommandScanner
{
    /// <summary>Command name used to invoke a user macro: <c>System.Macro(&lt;Name&gt;)</c>.</summary>
    public const string MacroCommandName = "System.Macro";

    /// <summary>
    /// JSON property names that hold a command (or, for <c>StripSegmentCommands</c>, a list of them).
    /// <c>Command</c> — <c>LoupedeckButton</c> / <c>ButtonState</c>;
    /// <c>PreCommands</c>/<c>PostCommands</c> — <c>CommandWrap</c>;
    /// <c>StripSegmentCommands</c> — free-draw side strips;
    /// <c>CommandString</c> — a macro's Command step.
    /// </summary>
    private static readonly HashSet<string> CommandProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Command",
        "PreCommands",
        "PostCommands",
        "StripSegmentCommands",
        "CommandString"
    };

    /// <summary>The property whose value is a list of commands, one per strip segment.</summary>
    private const string ListProperty = "StripSegmentCommands";

    /// <summary>True when a JSON property holds a command (or a list of them).</summary>
    public static bool IsCommandProperty(string propertyName) =>
        propertyName != null && CommandProperties.Contains(propertyName);

    /// <summary>True when the property's value is a list of commands rather than one command.</summary>
    public static bool IsCommandListProperty(string propertyName) =>
        string.Equals(propertyName, ListProperty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The command tokens of one command property: the value itself, or each element for the
    /// list property. A token is a legacy string or an array of calls.
    /// </summary>
    public static IEnumerable<JToken> CommandTokensOf(JProperty property)
    {
        if (property?.Value == null)
            return [];

        // Not a collection expression: next to a JArray it would become a new JArray holding a copy
        // of the value, and a rewrite would then change the copy.
        return IsCommandListProperty(property.Name) && property.Value is JArray list
            ? list
            : new[] { property.Value };
    }

    /// <summary>
    /// Returns every command stored anywhere below <paramref name="root"/>, in document order and
    /// including duplicates (the caller decides how to aggregate). Empty ones are skipped.
    /// </summary>
    public static IEnumerable<CommandBinding> HarvestCommands(JToken root)
    {
        if (root is not JContainer container)
            yield break;

        foreach (JProperty property in container.DescendantsAndSelf().OfType<JProperty>())
        {
            if (!CommandProperties.Contains(property.Name))
                continue;

            foreach (JToken token in CommandTokensOf(property))
            {
                CommandBinding command = token.Type is JTokenType.String or JTokenType.Array
                    ? CommandBindingJsonConverter.FromToken(token)
                    : null;
                if (!CommandBinding.IsNullOrEmpty(command))
                    yield return command;
            }
        }
    }

    /// <summary>
    /// Splits the command strings below <paramref name="root"/> into distinct command names,
    /// e.g. <c>"System.GotoPage(1) &amp;&amp; Obs.SetScene(Live)"</c> contributes
    /// <c>System.GotoPage</c> and <c>Obs.SetScene</c>.
    /// </summary>
    public static HashSet<string> CollectCommandNames(JToken root)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (CommandBinding command in HarvestCommands(root))
        {
            foreach (CommandCall call in command)
            {
                if (!string.IsNullOrWhiteSpace(call.Name))
                    names.Add(call.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Returns the macro names referenced via <c>System.Macro(&lt;Name&gt;)</c> anywhere below
    /// <paramref name="root"/>. Comparison of the names themselves is case-insensitive, matching
    /// <c>IMacroManager.Get</c>.
    /// </summary>
    public static HashSet<string> CollectMacroNames(JToken root)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (CommandBinding command in HarvestCommands(root))
        {
            foreach (CommandCall call in command)
            {
                if (!string.Equals(call.Name, MacroCommandName, StringComparison.Ordinal))
                    continue;

                string[] parameters = call.GetParameters();
                if (parameters.Length > 0 && !string.IsNullOrWhiteSpace(parameters[0]))
                    names.Add(parameters[0]);
            }
        }

        return names;
    }

    /// <summary>
    /// Returns the plugin ids bound to a side strip (<c>RotaryButtonPage.StripPluginId</c>) below
    /// <paramref name="root"/>. A strip provider is referenced by plugin id only — no command
    /// points at it — so a command-driven plugin scan would miss it entirely.
    /// </summary>
    public static HashSet<string> CollectStripPluginIds(JToken root)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);

        if (root is not JContainer container)
            return ids;

        foreach (JProperty property in container.DescendantsAndSelf().OfType<JProperty>())
        {
            if (!string.Equals(property.Name, "StripPluginId", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value is JValue { Type: JTokenType.String } value &&
                !string.IsNullOrWhiteSpace((string)value.Value))
            {
                ids.Add((string)value.Value);
            }
        }

        return ids;
    }
}
