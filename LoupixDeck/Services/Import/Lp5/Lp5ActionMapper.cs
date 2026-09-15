using LoupixDeck.Commands;
using LoupixDeck.Services.Macros;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>Why a Loupedeck action could not be carried over.</summary>
public enum Lp5UnmappedReason
{
    /// <summary>The action belongs to a Loupedeck plugin; <c>Detail</c> names the plugin.</summary>
    PluginAction,

    /// <summary>A built-in or template action without a LoupixDeck equivalent; <c>Detail</c> names it.</summary>
    UnsupportedAction,

    /// <summary>A shortcut uses a key LoupixDeck cannot send; <c>Detail</c> is the shortcut.</summary>
    UnknownKey,

    /// <summary>The action references a definition the file does not contain.</summary>
    MissingDefinition,

    /// <summary>A dial adjustment sits on a key, or a key action on a dial rotation.</summary>
    WrongControl
}

public enum Lp5StepKind
{
    Keys,
    Text,
    Command
}

/// <summary>One primitive step of a Loupedeck action, already translated.</summary>
public sealed record Lp5Step(Lp5StepKind Kind, string Value);

/// <summary>A user macro the import creates because the action cannot be expressed as one command.</summary>
public sealed record Lp5MacroDraft(string Name, IReadOnlyList<Lp5Step> Steps);

/// <summary>Result of mapping a press action.</summary>
public sealed record Lp5PressMapping(string Command, string Label, Lp5UnmappedReason? Reason, string Detail)
{
    public bool IsMapped => Command != null;
}

/// <summary>Result of mapping a rotate action: one command per direction.</summary>
public sealed record Lp5RotateMapping(string LeftCommand, string RightCommand, string Label, Lp5UnmappedReason? Reason, string Detail)
{
    public bool IsMapped => LeftCommand != null || RightCommand != null;
}

/// <summary>
/// Maps Loupedeck action references to LoupixDeck command strings. Anything a single command can
/// express becomes that command; a multi-step action or text the command parser cannot carry becomes
/// a user macro (collected in <see cref="Macros"/>) invoked through <c>System.Macro</c>.
/// </summary>
/// <remarks>
/// Action references seen in real exports:
/// <c>$@Generic___@ProfileAction___&lt;id&gt;</c> (keyboard shortcut / character / text, defined in
/// <c>profileActions</c>), <c>$@Generic___@Macro___&lt;id&gt;</c> (a list of primitive actions),
/// <c>$@Generic___@MacroAdjustment___&lt;id&gt;</c> (dial: left / right action lists),
/// <c>$@Generic___@MouseWheel</c>, <c>$DefaultWin___&lt;name&gt;</c> (media and volume) and
/// <c>$&lt;Plugin&gt;___…</c> for plugin actions.
/// </remarks>
public sealed class Lp5ActionMapper(Lp5Package package, Func<string, bool> isMacroNameTaken)
{
    private const string Generic = "$@Generic___@";
    private const string ProfileActionPrefix = Generic + "ProfileAction___";
    private const string MacroPrefix = Generic + "Macro___";
    private const string MacroAdjustmentPrefix = Generic + "MacroAdjustment___";
    private const string MouseWheel = Generic + "MouseWheel";
    private const string DefaultPluginPrefix = "$DefaultWin___";
    private const string DefaultMacPluginPrefix = "$DefaultMac___";

    /// <summary>Longest text sent through <c>System.SimpleMacro</c> directly instead of a macro.</summary>
    private const int MaxInlineTextLength = 200;

    private readonly List<Lp5MacroDraft> _macros = [];
    private readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Lp5PressMapping> _pressCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lp5RotateMapping> _rotateCache = new(StringComparer.Ordinal);

    /// <summary>The macros the mapped commands refer to, in creation order.</summary>
    public IReadOnlyList<Lp5MacroDraft> Macros => _macros;

    // ───────── Press ─────────

    public Lp5PressMapping MapPress(string action)
    {
        if (string.IsNullOrEmpty(action))
            return null;

        if (_pressCache.TryGetValue(action, out Lp5PressMapping cached))
            return cached;

        Lp5PressMapping mapping = MapPressCore(action);
        _pressCache[action] = mapping;
        return mapping;
    }

    private Lp5PressMapping MapPressCore(string action)
    {
        if (action.StartsWith(MacroPrefix, StringComparison.Ordinal))
        {
            if (!package.MacroCommands.TryGetValue(action[MacroPrefix.Length..], out JObject macro))
                return Unmapped(action, null, Lp5UnmappedReason.MissingDefinition, null);

            string label = Lp5Package.Text(macro["displayName"]);
            List<string> primitives = (macro["actions"] as JArray ?? []).Select(Lp5Package.Text).Where(s => s.Length > 0).ToList();
            return FromPrimitives(action, label, primitives);
        }

        if (action.StartsWith(MacroAdjustmentPrefix, StringComparison.Ordinal))
        {
            string label = package.MacroAdjustments.TryGetValue(action[MacroAdjustmentPrefix.Length..], out JObject adjustment)
                ? Lp5Package.Text(adjustment["displayName"])
                : null;
            return Unmapped(action, label, Lp5UnmappedReason.WrongControl, null);
        }

        return FromPrimitives(action, null, [action]);
    }

    private Lp5PressMapping FromPrimitives(string action, string label, IReadOnlyList<string> primitives)
    {
        List<Lp5Step> steps = [];
        foreach (string primitive in primitives)
        {
            Lp5StepResult result = MapPrimitive(primitive);
            if (result.Step == null)
                return Unmapped(action, FirstNonEmpty(label, result.Label), result.Reason, result.Detail);

            label = FirstNonEmpty(label, result.Label);
            steps.Add(result.Step);
        }

        if (steps.Count == 0)
            return Unmapped(action, label, Lp5UnmappedReason.UnsupportedAction, null);

        label = FirstNonEmpty(label, DescribeAction(action));
        return new Lp5PressMapping(ToCommand(steps, label), label, null, null);
    }

    // ───────── Rotate ─────────

    public Lp5RotateMapping MapRotate(string action)
    {
        if (string.IsNullOrEmpty(action))
            return null;

        if (_rotateCache.TryGetValue(action, out Lp5RotateMapping cached))
            return cached;

        Lp5RotateMapping mapping = MapRotateCore(action);
        _rotateCache[action] = mapping;
        return mapping;
    }

    private Lp5RotateMapping MapRotateCore(string action)
    {
        if (action == MouseWheel)
            return new Lp5RotateMapping("System.MouseScroll(1)", "System.MouseScroll(-1)", "Scroll", null, null);

        if (TryBuiltInName(action, out string builtIn))
        {
            if (string.Equals(builtIn, "Volume", StringComparison.OrdinalIgnoreCase))
                return new Lp5RotateMapping("System.KeyCombination(VolumeDown)", "System.KeyCombination(VolumeUp)", "Volume", null, null);

            return new Lp5RotateMapping(null, null, Humanize(builtIn), Lp5UnmappedReason.UnsupportedAction, builtIn);
        }

        if (action.StartsWith(MacroAdjustmentPrefix, StringComparison.Ordinal))
        {
            if (!package.MacroAdjustments.TryGetValue(action[MacroAdjustmentPrefix.Length..], out JObject adjustment))
                return new Lp5RotateMapping(null, null, null, Lp5UnmappedReason.MissingDefinition, null);

            string label = FirstNonEmpty(Lp5Package.Text(adjustment["displayName"]), "Dial");
            Lp5PressMapping left = FromPrimitives(action + "#left", label + " (left)", Primitives(adjustment["actionsLeft"]));
            Lp5PressMapping right = FromPrimitives(action + "#right", label + " (right)", Primitives(adjustment["actionsRight"]));

            if (!left.IsMapped && !right.IsMapped)
                return new Lp5RotateMapping(null, null, label, left.Reason ?? right.Reason, left.Detail ?? right.Detail);

            return new Lp5RotateMapping(left.Command, right.Command, label, null, null);
        }

        if (action.StartsWith(ProfileActionPrefix, StringComparison.Ordinal) ||
            action.StartsWith(MacroPrefix, StringComparison.Ordinal))
        {
            Lp5PressMapping press = MapPress(action);
            return new Lp5RotateMapping(null, null, press?.Label, Lp5UnmappedReason.WrongControl, null);
        }

        Lp5StepResult plugin = MapPrimitive(action);
        return new Lp5RotateMapping(null, null, plugin.Label, plugin.Reason ?? Lp5UnmappedReason.UnsupportedAction, plugin.Detail);
    }

    private static List<string> Primitives(JToken array) =>
        (array as JArray ?? []).Select(Lp5Package.Text).Where(s => s.Length > 0).ToList();

    // ───────── Primitives ─────────

    private readonly record struct Lp5StepResult(Lp5Step Step, string Label, Lp5UnmappedReason? Reason, string Detail);

    private Lp5StepResult MapPrimitive(string primitive)
    {
        if (primitive.StartsWith(ProfileActionPrefix, StringComparison.Ordinal))
        {
            if (!package.ProfileActions.TryGetValue(primitive, out JObject definition))
                return new Lp5StepResult(null, null, Lp5UnmappedReason.MissingDefinition, null);

            return MapTemplateAction(definition);
        }

        if (TrySplitGeneric(primitive, out string verb, out string argument))
        {
            switch (verb)
            {
                case "KeyboardShortcut":
                    return KeysStep(argument, null);

                case "TypeText":
                case "SendText":
                    return string.IsNullOrEmpty(argument)
                        ? new Lp5StepResult(null, null, Lp5UnmappedReason.UnsupportedAction, verb)
                        : new Lp5StepResult(new Lp5Step(Lp5StepKind.Text, argument), null, null, null);

                case "ExecuteApplication":
                    return LaunchStep(argument);

                default:
                    return new Lp5StepResult(null, Humanize(verb), Lp5UnmappedReason.UnsupportedAction, verb);
            }
        }

        if (TryBuiltInName(primitive, out string builtIn))
        {
            string key = Lp5KeyNames.TranslateKey(builtIn);
            return key != null && !string.Equals(builtIn, "Volume", StringComparison.OrdinalIgnoreCase)
                ? new Lp5StepResult(new Lp5Step(Lp5StepKind.Keys, key), Humanize(builtIn), null, null)
                : new Lp5StepResult(null, Humanize(builtIn), Lp5UnmappedReason.UnsupportedAction, builtIn);
        }

        string pluginName = PluginName(primitive);
        return pluginName != null
            ? new Lp5StepResult(null, pluginName, Lp5UnmappedReason.PluginAction, pluginName)
            : new Lp5StepResult(null, null, Lp5UnmappedReason.UnsupportedAction, primitive);
    }

    /// <summary>A <c>profileActions</c> entry: a template (<c>@KeyboardKey</c>, …) plus its parameters.</summary>
    private Lp5StepResult MapTemplateAction(JObject definition)
    {
        string label = Lp5Package.Text(definition["displayName"]);
        string template = Lp5Package.Text(definition["templateActionName"]);
        string verb = template[(template.LastIndexOf('@') + 1)..];
        JToken parameters = definition["actionParameters"]?["parameters"];

        switch (verb)
        {
            case "KeyboardKey":
            {
                // "ControlOrCommand+KeyT___67699721___Ctrl+T___win-…": the first segment is the
                // layout-independent key code, the later ones are display text and OS scan codes.
                string encoded = Lp5Package.Text(parameters?["keyboardKey"]);
                int separator = encoded.IndexOf("___", StringComparison.Ordinal);
                return KeysStep(separator < 0 ? encoded : encoded[..separator], label);
            }

            case "KeyboardChar":
            {
                // A UTF-16 code unit in hex, e.g. "0031" for '1'.
                string hex = Lp5Package.Text(parameters?["keyboardKey"]);
                return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code) &&
                       code is > 0 and (< 0xD800 or > 0xDFFF) and <= 0x10FFFF
                    ? new Lp5StepResult(new Lp5Step(Lp5StepKind.Text, char.ConvertFromUtf32(code)), label, null, null)
                    : new Lp5StepResult(null, label, Lp5UnmappedReason.UnsupportedAction, verb);
            }

            case "SendText":
            case "TypeText":
            {
                string text = Lp5Package.Text(parameters?["text"]);
                return text.Length > 0
                    ? new Lp5StepResult(new Lp5Step(Lp5StepKind.Text, text), label, null, null)
                    : new Lp5StepResult(null, label, Lp5UnmappedReason.UnsupportedAction, verb);
            }

            default:
                return new Lp5StepResult(null, label, Lp5UnmappedReason.UnsupportedAction, Humanize(verb));
        }
    }

    private static Lp5StepResult KeysStep(string combination, string label)
    {
        string keys = Lp5KeyNames.TranslateCombination(combination);
        return keys != null
            ? new Lp5StepResult(new Lp5Step(Lp5StepKind.Keys, keys), label, null, null)
            : new Lp5StepResult(null, label, Lp5UnmappedReason.UnknownKey, combination);
    }

    private static Lp5StepResult LaunchStep(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new Lp5StepResult(null, null, Lp5UnmappedReason.UnsupportedAction, "ExecuteApplication");

        target = target.Trim();
        bool isWeb = Uri.TryCreate(target, UriKind.Absolute, out Uri uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        string command = isWeb
            ? $"{OpenUrlCommand.CommandName}({CommandParameterEncoding.Encode(uri.AbsoluteUri)})"
            : $"System.LaunchApp({CommandParameterEncoding.Encode(target)})";

        return new Lp5StepResult(new Lp5Step(Lp5StepKind.Command, command), isWeb ? uri.Host : Path.GetFileNameWithoutExtension(target), null, null);
    }

    // ───────── Steps → command ─────────

    private string ToCommand(IReadOnlyList<Lp5Step> steps, string label)
    {
        if (steps.Count == 1)
        {
            Lp5Step step = steps[0];
            switch (step.Kind)
            {
                case Lp5StepKind.Keys:
                    return $"System.KeyCombination({step.Value})";
                case Lp5StepKind.Command:
                    return step.Value;
                case Lp5StepKind.Text when CanInlineText(step.Value):
                    return $"System.SimpleMacro({step.Value})";
            }
        }
        else if (steps.All(s => s.Kind == Lp5StepKind.Keys))
        {
            return $"System.KeySequence({string.Join(",", steps.Select(s => s.Value))})";
        }

        string name = ReserveMacroName(label);
        _macros.Add(new Lp5MacroDraft(name, steps));
        return $"System.Macro({name})";
    }

    /// <summary>
    /// <c>System.SimpleMacro</c> does not decode its parameter, and the parser splits on <c>,</c>,
    /// stops at <c>)</c> and trims — such text only survives inside a macro's text step.
    /// </summary>
    private static bool CanInlineText(string text) =>
        text.Length <= MaxInlineTextLength &&
        text.IndexOfAny([',', '(', ')', '&', '\r', '\n']) < 0 &&
        text == text.Trim();

    private string ReserveMacroName(string label)
    {
        string baseName = SanitizeMacroName($"{package.Name} - {FirstNonEmpty(label, "Action")}");
        string name = baseName;
        for (int suffix = 2; isMacroNameTaken(name) || !_reservedNames.Add(name); suffix++)
            name = $"{baseName} {suffix}";

        return name;
    }

    private static string SanitizeMacroName(string name)
    {
        char[] chars = name.Select(c => MacroManager.HasValidNameCharacters(c.ToString()) ? c : ' ').ToArray();
        string cleaned = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > 0 ? cleaned : "Loupedeck";
    }

    // ───────── Helpers ─────────

    private static Lp5PressMapping Unmapped(string action, string label, Lp5UnmappedReason? reason, string detail) =>
        new(null, FirstNonEmpty(label, DescribeAction(action)), reason ?? Lp5UnmappedReason.UnsupportedAction, detail);

    private static bool TrySplitGeneric(string action, out string verb, out string argument)
    {
        verb = null;
        argument = string.Empty;
        if (!action.StartsWith(Generic, StringComparison.Ordinal))
            return false;

        string rest = action[Generic.Length..];
        int separator = rest.IndexOf("___", StringComparison.Ordinal);
        verb = separator < 0 ? rest : rest[..separator];
        argument = separator < 0 ? string.Empty : rest[(separator + 3)..];
        return true;
    }

    private static bool TryBuiltInName(string action, out string name)
    {
        foreach (string prefix in (ReadOnlySpan<string>)[DefaultPluginPrefix, DefaultMacPluginPrefix])
        {
            if (action.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = action[prefix.Length..];
                return name.Length > 0;
            }
        }

        name = null;
        return false;
    }

    /// <summary>"$VolumeControl___Loupedeck.…" → "VolumeControl"; null for anything that is not a plugin reference.</summary>
    private static string PluginName(string action)
    {
        if (!action.StartsWith('$') || action.StartsWith(Generic, StringComparison.Ordinal))
            return null;

        int separator = action.IndexOf("___", StringComparison.Ordinal);
        return separator > 1 ? action[1..separator] : null;
    }

    /// <summary>A readable fallback label for an action without a display name.</summary>
    private static string DescribeAction(string action)
    {
        if (TryBuiltInName(action, out string builtIn))
            return Humanize(builtIn);

        if (TrySplitGeneric(action, out string verb, out _))
            return Humanize(verb);

        return PluginName(action) ?? string.Empty;
    }

    /// <summary>"MediaPlayPause" → "Media Play Pause".</summary>
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        System.Text.StringBuilder builder = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                builder.Append(' ');
            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    private static string FirstNonEmpty(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;
}
