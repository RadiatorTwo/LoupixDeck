using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Finds the command strings inside a serialized config subtree (or macro document) and dissects
/// them into command names and macro references. Used by the profile package exporter to work out
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
    /// JSON property names that hold a command string (or an array of them).
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

    /// <summary>
    /// Returns every command string stored anywhere below <paramref name="root"/>, in document
    /// order and including duplicates (the caller decides how to aggregate).
    /// </summary>
    public static IEnumerable<string> HarvestCommandStrings(JToken root)
    {
        if (root is not JContainer container)
            yield break;

        foreach (JProperty property in container.DescendantsAndSelf().OfType<JProperty>())
        {
            if (!CommandProperties.Contains(property.Name))
                continue;

            foreach (string value in ValuesOf(property.Value))
                yield return value;
        }
    }

    private static IEnumerable<string> ValuesOf(JToken token)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } value when !string.IsNullOrWhiteSpace((string)value.Value):
                yield return (string)value.Value;
                break;

            case JArray array:
                foreach (JValue entry in array.OfType<JValue>())
                {
                    if (entry.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)entry.Value))
                        yield return (string)entry.Value;
                }

                break;
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

        foreach (string command in HarvestCommandStrings(root))
        {
            foreach (string segment in CommandStringParser.SplitChain(command))
            {
                string name = CommandStringParser.GetName(segment);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
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

        foreach (string command in HarvestCommandStrings(root))
        {
            foreach (string segment in CommandStringParser.SplitChain(command))
            {
                if (!string.Equals(CommandStringParser.GetName(segment), MacroCommandName, StringComparison.Ordinal))
                    continue;

                string[] parameters = CommandStringParser.GetParameters(segment);
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
