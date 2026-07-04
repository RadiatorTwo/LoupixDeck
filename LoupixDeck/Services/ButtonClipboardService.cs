using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

/// <summary>
/// The kind of element a clipboard snapshot came from. Paste is only allowed onto the same
/// kind (issue #166 compatibility rules) — the payloads are structurally different and cross
/// pasting would misplace or drop content.
/// </summary>
public enum ButtonKind
{
    Touch,
    Simple,
    Rotary,
    SideDisplay
}

/// <summary>
/// In-app clipboard for copy/paste of buttons and side displays (issue #166). Holds a JSON
/// <b>snapshot</b> of the copied element (independent of the source object, so it survives page /
/// device switches and later source edits), plus the source kind for compatibility checks.
/// Registered as a root singleton and forwarded into every device provider, so copy/paste works
/// across pages and devices.
/// </summary>
public interface IButtonClipboardService
{
    bool HasData { get; }
    ButtonKind? CurrentKind { get; }

    /// <summary>Raised when the clipboard content changes (set/clear).</summary>
    event Action Changed;

    /// <summary>Take a deep snapshot of <paramref name="source"/> as the given kind. Identity
    /// fields (button Index / physical Id) are stripped so paste keeps the target's own.</summary>
    void Copy(LoupedeckButton source, ButtonKind kind);

    /// <summary>True when a snapshot exists and it can be pasted onto a target of this kind.</summary>
    bool CanPasteInto(ButtonKind targetKind);

    /// <summary>Apply the current snapshot onto <paramref name="target"/> in place (keeps the
    /// target instance, its Index/Id and its ItemChanged subscription), then re-wires and
    /// refreshes it. Caller must have checked <see cref="CanPasteInto"/>.</summary>
    void PasteInto(LoupedeckButton target);

    void Clear();

    /// <summary>True when a button/side-display currently holds no user configuration, so a paste
    /// can overwrite it without asking (issue #166 empty-vs-modified rule).</summary>
    bool IsEmpty(LoupedeckButton button);
}

public sealed class ButtonClipboardService : IButtonClipboardService
{
    // Same converter set the config uses, so a snapshot round-trips identically (colors + the
    // polymorphic layer format). SKBitmap fields on the models are [JsonIgnore], so the bitmap
    // converter is only here for parity/safety.
    private static readonly JsonSerializerSettings CloneSettings = CreateCloneSettings();
    private static readonly JsonSerializer CloneSerializer = JsonSerializer.Create(CloneSettings);

    private static JsonSerializerSettings CreateCloneSettings()
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new ColorJsonConverter());
        settings.Converters.Add(new SKBitmapBase64Converter());
        settings.Converters.Add(new LayerJsonConverter());
        return settings;
    }

    private ButtonKind _kind;
    private string _json;

    public bool HasData => _json != null;
    public ButtonKind? CurrentKind => _json != null ? _kind : null;

    public event Action Changed;

    public void Copy(LoupedeckButton source, ButtonKind kind)
    {
        if (source == null) return;

        var jo = JObject.FromObject(source, CloneSerializer);

        // Strip the target-owned identity fields so paste never moves them: TouchButton/
        // RotaryButton carry a positional "Index"; SimpleButton carries a physical hardware "Id".
        jo.Remove("Index");
        jo.Remove("Id");

        _kind = kind;
        _json = jo.ToString(Formatting.None);
        Changed?.Invoke();
    }

    public bool CanPasteInto(ButtonKind targetKind) => _json != null && _kind == targetKind;

    public void PasteInto(LoupedeckButton target)
    {
        if (target == null || _json == null) return;

        JsonConvert.PopulateObject(_json, target, CloneSettings);

        // Re-attach the handlers/active-state wiring the JSON converters bypass, then repaint.
        switch (target)
        {
            case TouchButton touch:
                touch.RewireLayerHandlers();
                break;
            case SimpleButton simple:
                simple.RewireAfterLoad();
                break;
        }

        target.Refresh();
    }

    public void Clear()
    {
        if (_json == null) return;
        _json = null;
        Changed?.Invoke();
    }

    public bool IsEmpty(LoupedeckButton button)
    {
        switch (button)
        {
            case TouchButton touch:
                return touch.States.Count <= 1
                       && (touch.ActiveState?.Layers?.Count ?? 0) == 0
                       && touch.BackColor == Colors.Black
                       && !touch.VibrationEnabled
                       && string.IsNullOrEmpty(touch.Command);

            case SimpleButton simple:
                return simple.States.Count <= 1
                       && simple.ButtonColor == Colors.Black
                       && string.IsNullOrEmpty(simple.Command);

            case RotaryButton rotary:
                return string.IsNullOrEmpty(rotary.Command)
                       && string.IsNullOrEmpty(rotary.RotaryLeftCommand)
                       && string.IsNullOrEmpty(rotary.RotaryRightCommand)
                       && string.IsNullOrEmpty(rotary.DisplayText);

            default:
                return button == null;
        }
    }
}
