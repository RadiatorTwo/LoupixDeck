using LoupixDeck.Localization;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>The workspace an action is resolved in; page switches are relative to it.</summary>
internal sealed record Lp5Context(string WorkspaceId, IReadOnlyList<string> TouchPageNames,
    IReadOnlyList<string> EncoderPageNames)
{
    /// <summary>The touch page the action sits on; null for dials and round buttons.</summary>
    public string PageId { get; init; }

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
    private const string KeyboardCharTemplate = "$@Generic___@KeyboardChar";
    private const string MouseWheelTemplate = "$@Generic___@MouseWheelExt";
    private const string MouseWheel = "$@Generic___@MouseWheel";
    private const string ShortcutPrefix = "$@Generic___@KeyboardShortcut___";
    private const string TypeTextPrefix = "$@Generic___@TypeText___";
    private const string GoBack = "$@Generic___@GoBack";
    private const string DefaultOutputPrefix = "$WinAudio___NotADoctor99.WinAudioPlugin.DefaultOutputDeviceCommand___";
    private const int MaxDepth = 8;
    private const int MaxLabelLength = 28;

    private readonly Dictionary<string, JObject> _profileActions;
    private readonly Dictionary<string, JObject> _macros;
    private readonly Dictionary<string, JObject> _adjustments;
    private readonly Dictionary<string, JObject> _workspaces;
    private readonly Dictionary<string, JObject> _touchPages;
    private readonly Dictionary<string, JObject> _encoderPages;
    private readonly IReadOnlyDictionary<string, Guid> _workspaceIds;
    private readonly Dictionary<string, (string Workspace, string Page)> _backTargets;
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
        _backTargets = BackTargets(archive);
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

            List<string> steps = Lp5Json.Strings(macro, "actions").Where(s => !string.IsNullOrEmpty(s)).ToList();
            string command = Chain(macro, steps, context, depth, null, out Lp5Resolution failed);
            if (command != null) return new Lp5Resolution(command);
            if (HasMissingSteps(macro, steps)) return new Lp5Resolution(null, Lp5UnsupportedReason.MissingMacroSteps, actionRef);

            // A one-step macro is just that step, so its own reason is the more precise one.
            return steps.Count == 1 && failed?.Reason != null
                ? failed
                : new Lp5Resolution(null, Lp5UnsupportedReason.UnsupportedMacro, failed?.Detail ?? actionRef);
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

        if (actionRef == GoBack)
            return ResolveGoBack(context);

        if (actionRef.StartsWith(ShortcutPrefix, StringComparison.Ordinal))
        {
            string combo = Lp5KeyCombo.Normalize(actionRef[ShortcutPrefix.Length..]);
            return combo != null
                ? new Lp5Resolution($"System.KeyCombination({combo})")
                : new Lp5Resolution(null, Lp5UnsupportedReason.MissingDefinition, actionRef);
        }

        if (actionRef.StartsWith(TypeTextPrefix, StringComparison.Ordinal))
            return TypeText(actionRef[TypeTextPrefix.Length..], actionRef);

        if (actionRef.StartsWith(DefaultOutputPrefix, StringComparison.Ordinal))
        {
            // Loupedeck names the output; the Audio plugin needs its endpoint id, found on this computer.
            string device = actionRef[DefaultOutputPrefix.Length..];
            return Lp5AudioDevices.RenderEndpointId(device) is { } endpoint
                ? new Lp5Resolution($"Audio.SetDefaultDevice({CommandParameterEncoding.Encode(endpoint)})")
                : new Lp5Resolution(null, Lp5UnsupportedReason.AudioDeviceNotFound, device);
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
            "$DefaultWin___MediaNextTrack" => new Lp5Resolution("System.KeyCombination(NextTrack)"),
            "$DefaultWin___MediaPrevTrack" => new Lp5Resolution("System.KeyCombination(PrevTrack)"),
            "$DefaultWin___MediaStop" => new Lp5Resolution("System.KeyCombination(MediaStop)"),
            // The press ("reset") of Loupedeck's volume dial toggles mute.
            "$DefaultWin___ResetVolume" => new Lp5Resolution("System.KeyCombination(Mute)"),
            // Loupedeck's "Windows actions" opens Quick Settings, which is Win+A.
            "$DefaultWin___WindowsActions" => new Lp5Resolution("System.KeyCombination(Win+A)"),
            "$@Generic___@MouseClick" => new Lp5Resolution("System.MouseClick(Left)"),
            "$@Generic___@NextTouchPage" => new Lp5Resolution("System.NextPage"),
            "$@Generic___@PreviousTouchPage" => new Lp5Resolution("System.PreviousPage"),
            "$@Generic___@NextEncoderPage" => new Lp5Resolution(_independentSides
                ? "System.NextRotaryPageLeft && System.NextRotaryPageRight"
                : "System.NextRotaryPage"),
            "$@Generic___@PreviousEncoderPage" => new Lp5Resolution(_independentSides
                ? "System.PreviousRotaryPageLeft && System.PreviousRotaryPageRight"
                : "System.PreviousRotaryPage"),
            _ => Lp5PluginActions.Command(actionRef) is { } pluginCommand
                ? new Lp5Resolution(pluginCommand)
                : new Lp5Resolution(null, Lp5UnsupportedReason.UnknownAction, actionRef)
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
                    left = Chain(adjustment, Lp5Json.Strings(adjustment, "actionsLeft"), context, 0, false, out _);
                    right = Chain(adjustment, Lp5Json.Strings(adjustment, "actionsRight"), context, 0, true, out _);
                    if (left == null && right == null)
                    {
                        rotateReason = HasMissingSteps(adjustment,
                            Lp5Json.Strings(adjustment, "actionsLeft").Concat(Lp5Json.Strings(adjustment, "actionsRight")))
                            ? Lp5UnsupportedReason.MissingMacroSteps
                            : Lp5UnsupportedReason.UnsupportedAdjustment;
                    }
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
            else if (Lp5PluginActions.Adjustment(rotateRef) is { } adjustment)
            {
                left = adjustment;
                right = adjustment;
            }
            else if (IsWheel(rotateRef))
            {
                left = WheelCommand(rotateRef, false);
                right = WheelCommand(rotateRef, true);
                if (left == null)
                    rotateReason = Lp5UnsupportedReason.UnsupportedAdjustment;
            }
            else if (Resolve(rotateRef, context).Command is { } command)
            {
                // A plain command on a dial turn runs on every step, whichever way the dial turns.
                left = command;
                right = command;
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
        if (actionRef == GoBack) return Loc.Tr("LoupedeckImport_LabelBack");

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

        // One typed character, stored as its hex code point ("0031" for "1").
        if (template == KeyboardCharTemplate)
        {
            return int.TryParse(Lp5Json.Str(parameters, "keyboardKey"), System.Globalization.NumberStyles.HexNumber,
                       System.Globalization.CultureInfo.InvariantCulture, out int codePoint)
                   && codePoint is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
                ? TypeText(char.ConvertFromUtf32(codePoint), template).Command
                : null;
        }

        // MouseMoveExt drags the pointer; LoupixDeck has no pointer-move command.
        return Lp5PluginActions.ProfileActionCommand(template, key => Lp5Json.Str(parameters, key));
    }

    /// <summary>True for a mouse-wheel adjustment, plain or as a profile action with options.</summary>
    private bool IsWheel(string actionRef) =>
        actionRef == MouseWheel ||
        (_profileActions.TryGetValue(actionRef, out JObject action) && Lp5Json.Str(action, "templateActionName") == MouseWheelTemplate);

    /// <summary>
    /// One wheel step, up for a right turn. The profile-action form can invert the direction and hold keys
    /// while scrolling; null for a horizontal wheel, which LoupixDeck cannot scroll.
    /// </summary>
    private string WheelCommand(string actionRef, bool right)
    {
        if (actionRef == MouseWheel)
            return right ? "System.MouseScroll(1)" : "System.MouseScroll(-1)";

        JObject parameters = Lp5Json.Obj(Lp5Json.Obj(_profileActions[actionRef], "actionParameters"), "parameters");
        if (Lp5Json.Bool(parameters, "isHorizontalWheel")) return null;

        bool up = right != Lp5Json.Bool(parameters, "isInverted");
        string keys = Lp5KeyCombo.Normalize(Lp5Json.Str(parameters, "keyboardKey"));
        return keys != null
            ? $"System.MouseCombo({keys},{(up ? "ScrollUp" : "ScrollDown")})"
            : $"System.MouseScroll({(up ? 1 : -1)})";
    }

    /// <summary>
    /// Types <paramref name="text"/>. The command parser splits parameters on <c>,</c> and cuts at
    /// <c>)</c>, and the text command does not decode escapes, so only text free of those converts.
    /// </summary>
    private static Lp5Resolution TypeText(string text, string actionRef)
    {
        if (text.Length == 0)
            return new Lp5Resolution(null, Lp5UnsupportedReason.EmptyText, actionRef);

        return text.IndexOfAny([',', '(', ')']) < 0 && !text.Contains("&&", StringComparison.Ordinal) && text.Trim() == text
            ? new Lp5Resolution($"System.SimpleMacro({text})")
            : new Lp5Resolution(null, Lp5UnsupportedReason.UnknownAction, actionRef);
    }

    /// <summary>
    /// Joins a macro's steps into one <c>&amp;&amp;</c> chain; null when any step cannot be converted,
    /// as a partial macro would do something different from the original. <paramref name="right"/> is
    /// the turn direction for an adjustment's steps, null for a command macro.
    /// </summary>
    private string Chain(JObject macro, IEnumerable<string> steps, Lp5Context context, int depth, bool? right,
        out Lp5Resolution failed)
    {
        failed = null;

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

            if (right is { } direction && IsWheel(step))
            {
                string wheel = WheelCommand(step, direction);
                if (wheel == null)
                {
                    failed = new Lp5Resolution(null, Lp5UnsupportedReason.UnsupportedAdjustment, step);
                    return null;
                }

                parts.Add(wheel);
                continue;
            }

            Lp5Resolution resolution = Resolve(step, context, depth + 1);
            if (resolution.Command == null)
            {
                failed = resolution;
                return null;
            }

            parts.Add(resolution.Command);
        }

        return parts.Count > 0 ? string.Join(" && ", parts) : null;
    }

    /// <summary>
    /// True when a step is a bare editor-command name that the macro does not define. Loupedeck
    /// sometimes saves a copied macro without its editor commands, leaving nothing to convert.
    /// </summary>
    private static bool HasMissingSteps(JObject macro, IEnumerable<string> steps)
    {
        HashSet<string> defined = Lp5Json.Arr(macro, "actionEditorCommands")
            .Select(editor => Lp5Json.Str(editor, "name"))
            .Where(name => name != null)
            .ToHashSet(StringComparer.Ordinal);

        return steps.Any(step => !string.IsNullOrEmpty(step) && !step.StartsWith('$') && !defined.Contains(step));
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

    /// <summary>
    /// Loupedeck's "go back" returns to the previous page. LoupixDeck keeps no page history, so it only
    /// converts when exactly one key leads to the page: then "back" can only mean that key's page.
    /// </summary>
    private Lp5Resolution ResolveGoBack(Lp5Context context)
    {
        if (context.PageId == null || !_backTargets.TryGetValue(context.PageId, out (string Workspace, string Page) back))
            return new Lp5Resolution(null, Lp5UnsupportedReason.AmbiguousGoBack, GoBack);

        int index = _workspaces.TryGetValue(back.Workspace, out JObject workspace)
            ? Lp5Json.Strings(workspace, "touchPageNames").ToList().IndexOf(back.Page)
            : -1;
        if (index < 0)
            return new Lp5Resolution(null, Lp5UnsupportedReason.AmbiguousGoBack, GoBack);

        string page = $"System.GotoPage({index + 1})";
        return back.Workspace != context.WorkspaceId && _workspaceIds.TryGetValue(back.Workspace, out Guid id)
            ? new Lp5Resolution($"System.GotoWorkspace({id}) && {page}")
            : new Lp5Resolution(page);
    }

    /// <summary>
    /// For every touch page opened by exactly one key, the workspace and page of that key. A page that is
    /// also opened from anywhere else (a second key, a macro, a dial) has no single way back and is left out.
    /// </summary>
    private Dictionary<string, (string Workspace, string Page)> BackTargets(Lp5Archive archive)
    {
        Dictionary<string, int> references = new(StringComparer.Ordinal);
        foreach (JValue value in archive.Profile.Descendants().OfType<JValue>())
        {
            if (value.Type == JTokenType.String && OpenedPage(value.ToString()) is { } target)
                references[target] = references.GetValueOrDefault(target) + 1;
        }

        Dictionary<string, List<(string Workspace, string Page)>> sources = new(StringComparer.Ordinal);
        Dictionary<string, JObject> touchPages = ByName(Lp5Json.Arr(archive.LayoutMode, "touchPages"));
        foreach (JObject workspace in _workspaces.Values)
        {
            string workspaceId = Lp5Json.Str(workspace, "name");
            foreach (string pageId in Lp5Json.Strings(workspace, "touchPageNames"))
            {
                if (pageId == null || !touchPages.TryGetValue(pageId, out JObject page)) continue;

                foreach (JToken control in Lp5Json.Arr(page, "controls"))
                {
                    if (OpenedPage(Lp5Json.Str(control, "pressAction")) is not { } target) continue;
                    if (!sources.TryGetValue(target, out List<(string Workspace, string Page)> list))
                        sources[target] = list = [];
                    list.Add((workspaceId, pageId));
                }
            }
        }

        Dictionary<string, (string Workspace, string Page)> result = new(StringComparer.Ordinal);
        foreach ((string target, List<(string Workspace, string Page)> list) in sources)
        {
            if (list.Count == 1 && references.GetValueOrDefault(target) == 1)
                result[target] = list[0];
        }

        return result;
    }

    /// <summary>The touch page a page or workspace switch opens; null for any other action.</summary>
    private string OpenedPage(string actionRef)
    {
        if (actionRef == null) return null;

        if (actionRef.StartsWith(TouchPagePrefix, StringComparison.Ordinal))
            return actionRef.Split('|')[^1];

        if (actionRef.StartsWith(WorkspacePrefix, StringComparison.Ordinal) &&
            _workspaces.TryGetValue(actionRef.Split('|')[^1], out JObject workspace))
        {
            return Lp5Json.Strings(workspace, "touchPageNames").FirstOrDefault();
        }

        return null;
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
