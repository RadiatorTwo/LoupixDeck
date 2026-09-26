using LoupixDeck.Localization;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>The workspace an action is resolved in; page switches are relative to it.</summary>
internal sealed record Lp5Context(string WorkspaceId, IReadOnlyList<string> TouchPageNames,
    IReadOnlyList<string> EncoderPageNames)
{
    public static readonly Lp5Context Global = new(null, [], []);
}

/// <summary>A converted action, or the reason it could not be converted.</summary>
internal sealed record Lp5Resolution(string Command, Lp5UnsupportedReason? Reason = null, string Detail = null)
{
    public static readonly Lp5Resolution None = new((string)null);
}

/// <summary>A converted dial: rotation commands, press command and strip label.</summary>
internal sealed record Lp5DialResolution(string Left, string Right, Lp5Resolution Press, string Label,
    Lp5UnsupportedReason? RotateReason, string RotateDetail);

/// <summary>
/// Maps Loupedeck action references to LoupixDeck command strings. Unknown actions are never guessed;
/// they come back with a reason instead.
/// </summary>
/// <remarks>
/// Ported from <c>lp5_to_loupix.py</c> of loupedeck-to-loupixdeck by Vencite (MIT license,
/// https://github.com/Vencite/loupedeck-to-loupixdeck, issue #289), whose mappings were checked
/// against real profiles.
/// </remarks>
internal sealed class Lp5ActionResolver
{
    private const string None = "$@Generic___@None";
    private const string ShortNone = "@None";
    private const string ProfileActionPrefix = "$@Generic___@ProfileAction___";
    private const string MacroPrefix = "$@Generic___@Macro___";
    private const string AdjustmentPrefix = "$@Generic___@MacroAdjustment___";
    private const string WorkspacePrefix = "$@Generic___@ChangeWorkspace___";
    private const string TouchPagePrefix = "$@Generic___@ChangeTouchPage___";
    private const string EncoderPagePrefix = "$@Generic___@ChangeEncoderPage___";
    private const string ExecutePrefix = "$@Generic___@ExecuteApplication___";
    private const string KeyboardTemplate = "$@Generic___@KeyboardKey";
    private const string MouseClickTemplate = "$@Generic___@MouseClickExt";
    private const int MaxDepth = 8;
    private const int MaxLabelLength = 28;

    private readonly Dictionary<string, JObject> _profileActions;
    private readonly Dictionary<string, JObject> _macros;
    private readonly Dictionary<string, JObject> _adjustments;
    private readonly Dictionary<string, JObject> _workspaces;
    private readonly Dictionary<string, JObject> _touchPages;
    private readonly Dictionary<string, JObject> _encoderPages;
    private readonly IReadOnlyDictionary<string, Guid> _workspaceIds;
    private readonly bool _independentSides;

    public Lp5ActionResolver(Lp5Archive archive, IReadOnlyDictionary<string, Guid> workspaceIds, bool independentSides)
    {
        _profileActions = ByName(Lp5Json.Arr(archive.Profile, "profileActions"));
        _macros = ByName(Lp5Json.Arr(archive.Profile, "macroCommands"));
        _adjustments = ByName(Lp5Json.Arr(archive.Profile, "macroAdjustments"));
        _workspaces = ByName(Lp5Json.Arr(archive.LayoutMode, "workspaces"));
        _touchPages = ByName(Lp5Json.Arr(archive.LayoutMode, "touchPages"));
        _encoderPages = ByName(Lp5Json.Arr(archive.LayoutMode, "encoderPages"));
        _workspaceIds = workspaceIds;
        _independentSides = independentSides;
    }

    /// <summary>True for an empty assignment.</summary>
    public static bool IsNone(string actionRef) =>
        string.IsNullOrEmpty(actionRef) || actionRef is None or ShortNone;

    public Lp5Resolution Resolve(string actionRef, Lp5Context context) => Resolve(actionRef, context, 0);

    private Lp5Resolution Resolve(string actionRef, Lp5Context context, int depth)
    {
        if (IsNone(actionRef)) return Lp5Resolution.None;
        if (depth > MaxDepth) return new Lp5Resolution(null, Lp5UnsupportedReason.RecursionLimit, actionRef);

        if (actionRef.StartsWith(ProfileActionPrefix, StringComparison.Ordinal))
        {
            if (!_profileActions.TryGetValue(actionRef, out JObject action))
                return new Lp5Resolution(null, Lp5UnsupportedReason.MissingDefinition, actionRef);

            string command = ProfileActionCommand(action);
            return command != null
                ? new Lp5Resolution(command)
                : new Lp5Resolution(null, Lp5UnsupportedReason.UnsupportedProfileAction,
                    Lp5Json.Str(action, "templateActionName") ?? actionRef);
        }

        if (actionRef.StartsWith(MacroPrefix, StringComparison.Ordinal))
        {
            if (!_macros.TryGetValue(LastSegment(actionRef), out JObject macro))
                return new Lp5Resolution(null, Lp5UnsupportedReason.MissingDefinition, actionRef);

            string command = Chain(macro, Lp5Json.Strings(macro, "actions"), context, depth);
            return command != null
                ? new Lp5Resolution(command)
                : new Lp5Resolution(null, Lp5UnsupportedReason.UnsupportedMacro, actionRef);
        }

        if (actionRef.StartsWith(WorkspacePrefix, StringComparison.Ordinal))
        {
            return _workspaceIds.TryGetValue(actionRef.Split('|')[^1], out Guid id)
                ? new Lp5Resolution($"System.GotoWorkspace({id})")
                : new Lp5Resolution(null, Lp5UnsupportedReason.MissingDefinition, actionRef);
        }

        if (actionRef.StartsWith(TouchPagePrefix, StringComparison.Ordinal))
            return PageSwitch(actionRef, context, "touchPageNames", context.TouchPageNames, n => $"System.GotoPage({n})");

        if (actionRef.StartsWith(EncoderPagePrefix, StringComparison.Ordinal))
        {
            return PageSwitch(actionRef, context, "encoderPageNames", context.EncoderPageNames, n => _independentSides
                ? $"System.GotoRotaryPageLeft({n}) && System.GotoRotaryPageRight({n})"
                : $"System.GotoRotaryPage({n})");
        }

        if (actionRef.StartsWith(ExecutePrefix, StringComparison.Ordinal))
        {
            string target = actionRef[ExecutePrefix.Length..];
            int end = target.IndexOf("||||", StringComparison.Ordinal);
            target = (end >= 0 ? target[..end] : target).Trim().Trim('"');
            return target.Length > 0
                ? new Lp5Resolution($"System.LaunchApp({CommandParameterEncoding.Encode(target)})")
                : new Lp5Resolution(null, Lp5UnsupportedReason.MissingDefinition, actionRef);
        }

        return actionRef switch
        {
            "$DefaultWin___MediaPlayPause" => new Lp5Resolution("System.KeyCombination(PlayPause)"),
            "$@Generic___@MouseClick" => new Lp5Resolution("System.MouseClick(Left)"),
            _ => new Lp5Resolution(null, Lp5UnsupportedReason.UnknownAction, actionRef)
        };
    }

    /// <summary>Resolves a dial: its press action and its rotation (left/right) action.</summary>
    public Lp5DialResolution ResolveDial(string pressRef, string rotateRef, Lp5Context context)
    {
        Lp5Resolution press = Resolve(pressRef, context);
        string label = Label(IsNone(rotateRef) ? pressRef : rotateRef);
        string left = null;
        string right = null;
        Lp5UnsupportedReason? rotateReason = null;

        if (!IsNone(rotateRef))
        {
            if (rotateRef.StartsWith(AdjustmentPrefix, StringComparison.Ordinal))
            {
                if (_adjustments.TryGetValue(LastSegment(rotateRef), out JObject adjustment))
                {
                    left = Chain(adjustment, Lp5Json.Strings(adjustment, "actionsLeft"), context, 0);
                    right = Chain(adjustment, Lp5Json.Strings(adjustment, "actionsRight"), context, 0);
                    if (left == null && right == null)
                        rotateReason = Lp5UnsupportedReason.UnsupportedAdjustment;
                }
                else
                {
                    rotateReason = Lp5UnsupportedReason.MissingDefinition;
                }
            }
            else if (rotateRef == "$DefaultWin___Volume")
            {
                left = "System.KeyCombination(VolumeDown)";
                right = "System.KeyCombination(VolumeUp)";
            }
            else if (rotateRef == "$@Generic___@MouseWheel")
            {
                left = "System.MouseScroll(-1)";
                right = "System.MouseScroll(1)";
            }
            else
            {
                // Pointer moves/drags and plugin adjustments have no core equivalent.
                rotateReason = Lp5UnsupportedReason.UnsupportedAdjustment;
            }
        }

        // A label for something that does nothing would mislead; leave the segment blank.
        if (press.Reason != null || rotateReason != null)
            label = string.Empty;

        return new Lp5DialResolution(left, right, press, label, rotateReason, rotateReason != null ? rotateRef : null);
    }

    /// <summary>The Loupedeck display name of an action, for key captions and the report.</summary>
    public string Label(string actionRef)
    {
        if (IsNone(actionRef)) return string.Empty;

        if (actionRef.StartsWith(ProfileActionPrefix, StringComparison.Ordinal))
            return DisplayName(_profileActions.GetValueOrDefault(actionRef)) ?? Loc.Tr("LoupedeckImport_FallbackProfileAction");
        if (actionRef.StartsWith(MacroPrefix, StringComparison.Ordinal))
            return DisplayName(_macros.GetValueOrDefault(LastSegment(actionRef))) ?? Loc.Tr("LoupedeckImport_FallbackMacro");
        if (actionRef.StartsWith(AdjustmentPrefix, StringComparison.Ordinal))
            return DisplayName(_adjustments.GetValueOrDefault(LastSegment(actionRef))) ?? Loc.Tr("LoupedeckImport_FallbackAdjustment");
        if (actionRef.StartsWith(WorkspacePrefix, StringComparison.Ordinal))
            return DisplayName(_workspaces.GetValueOrDefault(actionRef.Split('|')[^1])) ?? Loc.Tr("LoupedeckImport_FallbackWorkspace");
        if (actionRef.StartsWith(TouchPagePrefix, StringComparison.Ordinal))
            return DisplayName(_touchPages.GetValueOrDefault(actionRef.Split('|')[^1])) ?? Loc.Tr("LoupedeckImport_FallbackPage");
        if (actionRef.StartsWith(EncoderPagePrefix, StringComparison.Ordinal))
            return DisplayName(_encoderPages.GetValueOrDefault(actionRef.Split('|')[^1])) ?? Loc.Tr("LoupedeckImport_FallbackDials");

        // Plugin-style reference: the last segment is usually the readable action name.
        string tail = Uri.UnescapeDataString(LastSegment(actionRef));
        return tail.Length > MaxLabelLength ? tail[..MaxLabelLength] : tail;
    }

    private static string ProfileActionCommand(JObject action)
    {
        string template = Lp5Json.Str(action, "templateActionName") ?? string.Empty;
        JObject parameters = Lp5Json.Obj(Lp5Json.Obj(action, "actionParameters"), "parameters");

        if (template == KeyboardTemplate)
        {
            string combo = Lp5KeyCombo.Normalize(Lp5Json.Str(parameters, "keyboardKey"));
            return combo != null ? $"System.KeyCombination({combo})" : null;
        }

        if (template == MouseClickTemplate)
        {
            string button = Lp5Json.Str(parameters, "mouseButtonType") ?? "Left";
            string click = $"System.MouseClick({button})";
            if (Lp5Json.Bool(parameters, "isDoubleClick"))
                return $"{click} && {click}";

            string keys = Lp5KeyCombo.Normalize(Lp5Json.Str(parameters, "keyboardKey"));
            return keys != null ? $"System.MouseCombo({keys},{button})" : click;
        }

        // MouseMoveExt drags the pointer; LoupixDeck has no pointer-move command.
        return null;
    }

    /// <summary>
    /// Joins a macro's steps into one <c>&amp;&amp;</c> chain; null when any step cannot be converted,
    /// as a partial macro would do something different from the original.
    /// </summary>
    private string Chain(JObject macro, IEnumerable<string> steps, Lp5Context context, int depth)
    {
        // Keyboard steps are defined inline in the macro's own editor commands.
        Dictionary<string, string> local = new(StringComparer.Ordinal);
        foreach (JToken editor in Lp5Json.Arr(macro, "actionEditorCommands"))
        {
            string name = Lp5Json.Str(editor, "name");
            if (name == null || Lp5Json.Str(editor, "templateName") != KeyboardTemplate) continue;

            string combo = Lp5KeyCombo.Normalize(Lp5Json.Str(Lp5Json.Obj(editor, "actionParameters"), "keyboardKey"));
            if (combo != null)
                local[name] = $"System.KeyCombination({combo})";
        }

        List<string> parts = [];
        foreach (string step in steps)
        {
            if (string.IsNullOrEmpty(step)) continue;

            if (local.TryGetValue(step, out string keys))
            {
                parts.Add(keys);
                continue;
            }

            string command = Resolve(step, context, depth + 1).Command;
            if (command == null) return null;
            parts.Add(command);
        }

        return parts.Count > 0 ? string.Join(" && ", parts) : null;
    }

    /// <summary>
    /// A touch or dial page switch: <c>...___&lt;workspace&gt;|&lt;page&gt;</c>. A target in another
    /// workspace switches the workspace first.
    /// </summary>
    private Lp5Resolution PageSwitch(string actionRef, Lp5Context context, string namesKey,
        IReadOnlyList<string> contextNames, Func<int, string> pageCommand)
    {
        string[] parts = actionRef.Split('|');
        string target = parts[^1];
        string targetWorkspace = parts.Length >= 3 ? parts[^2] : null;

        IReadOnlyList<string> names = targetWorkspace != null && _workspaces.TryGetValue(targetWorkspace, out JObject ws)
            ? Lp5Json.Strings(ws, namesKey).ToList()
            : [];
        if (names.Count == 0)
            names = contextNames;

        int index = names.ToList().IndexOf(target);
        if (index < 0)
            return new Lp5Resolution(null, Lp5UnsupportedReason.PageOutsideWorkspace, actionRef);

        string page = pageCommand(index + 1);
        if (targetWorkspace != null && targetWorkspace != context.WorkspaceId &&
            _workspaceIds.TryGetValue(targetWorkspace, out Guid id))
        {
            return new Lp5Resolution($"System.GotoWorkspace({id}) && {page}");
        }

        return new Lp5Resolution(page);
    }

    private static string DisplayName(JObject obj) =>
        Lp5Json.Str(obj, "displayName") is { Length: > 0 } name ? name : null;

    private static string LastSegment(string actionRef)
    {
        int index = actionRef.LastIndexOf("___", StringComparison.Ordinal);
        return index >= 0 ? actionRef[(index + 3)..] : actionRef;
    }

    private static Dictionary<string, JObject> ByName(IEnumerable<JToken> items)
    {
        Dictionary<string, JObject> result = new(StringComparer.Ordinal);
        foreach (JObject item in items.OfType<JObject>())
        {
            if (Lp5Json.Str(item, "name") is { Length: > 0 } name)
                result[name] = item;
        }

        return result;
    }
}
