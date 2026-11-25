using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// In-place edits an imported payload needs beyond id remapping: macro references that had to be
/// renamed, and asset paths the local content-addressed store handed back under a different name.
/// </summary>
public static class PortablePayloadRewriter
{
    /// <summary>
    /// Property holding the command-derived key a plugin/text layer is bound to. It is a literal
    /// <c>name(p1,p2)</c> string, so a macro rename has to reach it too — otherwise the layer no
    /// longer matches its button's command and the next rescan sweeps it as an orphan.
    /// </summary>
    private const string OwnerKeyProperty = "OwnerKey";

    /// <summary>
    /// Rewrites <c>System.Macro(&lt;old&gt;)</c> to <c>System.Macro(&lt;new&gt;)</c> throughout the
    /// document. Only the macro command's own parameter is touched, so a macro name that also
    /// happens to appear in a page title or a text layer is left alone.
    /// </summary>
    public static void RenameMacroReferences(JObject payload, IReadOnlyDictionary<string, string> renames)
    {
        if (payload == null || renames == null || renames.Count == 0)
            return;

        RewriteCommands(payload, text => RenameInCommandChain(text, renames), call => RenameInCall(call, renames));
    }

    /// <summary>Prefix of the commands that address a companion by its device key.</summary>
    private const string CompanionCommandPrefix = "Companion.";

    /// <summary>
    /// Rewrites the companion device keys in <c>Companion.*</c> commands (<c>Companion.GotoTouchPage(&lt;key&gt;, …)</c>)
    /// from the exporting machine's keys to the companions chosen on import. Whole parameters are
    /// matched, never substrings: a slug-only key is a prefix of the same model's serial-scoped key.
    /// </summary>
    public static void RemapCompanionKeys(JObject payload, IReadOnlyDictionary<string, string> keys)
    {
        if (payload == null || keys == null || keys.Count == 0)
            return;

        RewriteCommands(payload, text => RemapKeysInCommandChain(text, keys), call => RemapKeysInCall(call, keys));
    }

    /// <summary>The structured counterpart of <see cref="RemapKeysInCommandChain"/>: whole values only.</summary>
    private static CommandCall RemapKeysInCall(CommandCall call, IReadOnlyDictionary<string, string> keys)
    {
        if (!call.Name.StartsWith(CompanionCommandPrefix, StringComparison.Ordinal))
            return call;

        string[] parameters = call.GetParameters();
        bool changed = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (keys.TryGetValue(parameters[i], out string newKey) && !string.IsNullOrEmpty(newKey))
            {
                parameters[i] = newKey;
                changed = true;
            }
        }

        return changed ? CommandCall.Create(call.Name, parameters) : call;
    }

    /// <summary>The structured counterpart of <see cref="RenameInCommandChain"/>.</summary>
    private static CommandCall RenameInCall(CommandCall call, IReadOnlyDictionary<string, string> renames)
    {
        if (!string.Equals(call.Name, PortableCommandScanner.MacroCommandName, StringComparison.Ordinal))
            return call;

        string[] parameters = call.GetParameters();
        if (parameters.Length == 0 || !renames.TryGetValue(parameters[0], out string newName))
            return call;

        parameters[0] = newName;
        return CommandCall.Create(call.Name, parameters);
    }

    /// <summary>
    /// Applies a rewrite to every command and layer owner key in <paramref name="payload"/>. A legacy
    /// string, and every text call of an array, goes through <paramref name="rewriteText"/>, exactly
    /// as before; a structured call goes through <paramref name="rewriteCall"/>, so its values never
    /// pass through the legacy syntax. A command in array form is written back in array form.
    /// </summary>
    private static void RewriteCommands(JObject payload, Func<string, string> rewriteText,
        Func<CommandCall, CommandCall> rewriteCall)
    {
        foreach (JProperty property in payload.DescendantsAndSelf().OfType<JProperty>().ToList())
        {
            if (string.Equals(property.Name, OwnerKeyProperty, StringComparison.OrdinalIgnoreCase))
            {
                RewriteStrings(property.Value, rewriteText);
                continue;
            }

            if (!PortableCommandScanner.IsCommandProperty(property.Name))
                continue;

            foreach (JToken token in PortableCommandScanner.CommandTokensOf(property).ToList())
            {
                switch (token)
                {
                    case JValue { Type: JTokenType.String } value when value.Value is string text:
                        value.Value = rewriteText(text);
                        break;

                    case JArray array:
                        JToken rewritten = RewriteArray(array, rewriteText, rewriteCall);
                        if (!ReferenceEquals(rewritten, array))
                            array.Replace(rewritten);
                        break;
                }
            }
        }
    }

    private static JToken RewriteArray(JArray array, Func<string, string> rewriteText,
        Func<CommandCall, CommandCall> rewriteCall)
    {
        CommandBinding command = CommandBindingJsonConverter.FromToken(array);
        List<CommandCall> calls = [];
        bool changed = false;

        foreach (CommandCall call in command)
        {
            CommandCall rewritten = call.IsText
                ? Rewrite(call)
                : rewriteCall(call);
            changed |= !ReferenceEquals(rewritten, call);
            calls.Add(rewritten);
        }

        return changed ? CommandBindingJsonConverter.ToToken(new CommandBinding(calls)) : array;

        CommandCall Rewrite(CommandCall call)
        {
            string text = rewriteText(call.Text);
            return string.Equals(text, call.Text, StringComparison.Ordinal) ? call : CommandCall.FromText(text);
        }
    }

    private static string RemapKeysInCommandChain(string command, IReadOnlyDictionary<string, string> keys)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            command.IndexOf(CompanionCommandPrefix, StringComparison.Ordinal) < 0)
        {
            return command;
        }

        List<string> segments = [];
        bool changed = false;

        foreach (string segment in CommandStringParser.SplitChain(command))
        {
            string name = CommandStringParser.GetName(segment);
            string[] parameters = CommandStringParser.GetParameters(segment);
            bool segmentChanged = false;

            if (name.StartsWith(CompanionCommandPrefix, StringComparison.Ordinal))
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (keys.TryGetValue(parameters[i], out string newKey) && !string.IsNullOrEmpty(newKey))
                    {
                        parameters[i] = newKey;
                        segmentChanged = true;
                    }
                }
            }

            segments.Add(segmentChanged ? $"{name}({string.Join(",", parameters)})" : segment);
            changed |= segmentChanged;
        }

        return changed ? string.Join(" && ", segments) : command;
    }

    /// <summary>
    /// Replaces stored asset paths with the paths the local asset store returned. The store is
    /// content-addressed, so the two normally match — this only catches the cases where they do
    /// not (e.g. a differing file extension), which would otherwise leave a dangling reference.
    /// </summary>
    public static void RemapAssetPaths(JObject payload, IReadOnlyDictionary<string, string> assetMap)
    {
        if (payload == null || assetMap == null || assetMap.Count == 0)
            return;

        foreach (JValue value in payload.DescendantsAndSelf().OfType<JValue>().ToList())
        {
            if (value.Type != JTokenType.String)
                continue;

            string text = (string)value.Value;
            if (AssetPathHarvester.IsAssetPath(text) && assetMap.TryGetValue(text, out string replacement))
                value.Value = replacement;
        }
    }

    private static void RewriteStrings(JToken token, Func<string, string> rewrite)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } value when value.Value is string text:
                value.Value = rewrite(text);
                break;

            case JArray array:
                foreach (JValue entry in array.OfType<JValue>())
                {
                    if (entry.Type == JTokenType.String && entry.Value is string entryText)
                        entry.Value = rewrite(entryText);
                }

                break;
        }
    }

    private static string RenameInCommandChain(string command, IReadOnlyDictionary<string, string> renames)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            command.IndexOf(PortableCommandScanner.MacroCommandName, StringComparison.Ordinal) < 0)
        {
            return command;
        }

        List<string> segments = [];
        bool changed = false;

        foreach (string segment in CommandStringParser.SplitChain(command))
        {
            string name = CommandStringParser.GetName(segment);
            string[] parameters = CommandStringParser.GetParameters(segment);

            if (string.Equals(name, PortableCommandScanner.MacroCommandName, StringComparison.Ordinal) &&
                parameters.Length > 0 &&
                renames.TryGetValue(parameters[0], out string newName))
            {
                parameters[0] = newName;
                segments.Add($"{name}({string.Join(",", parameters)})");
                changed = true;
            }
            else
            {
                segments.Add(segment);
            }
        }

        return changed ? string.Join(" && ", segments) : command;
    }
}
