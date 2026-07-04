using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

/// <summary>
/// Serialize/apply codec for copying a button's content onto another button of the same kind
/// (issue #166). Shared by the copy/paste clipboard and drag &amp; drop. A snapshot is a JSON
/// deep copy with the target-owned identity fields (positional <c>Index</c> / physical <c>Id</c>)
/// stripped, so applying it never moves the target's identity — only its configuration.
/// </summary>
public static class ButtonSnapshot
{
    // Same converter set the config uses, so a snapshot round-trips identically (colors + the
    // polymorphic layer format). SKBitmap fields on the models are [JsonIgnore], so the bitmap
    // converter is only here for parity/safety.
    private static readonly JsonSerializerSettings Settings = CreateSettings();
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

    private static JsonSerializerSettings CreateSettings()
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new ColorJsonConverter());
        settings.Converters.Add(new SKBitmapBase64Converter());
        settings.Converters.Add(new LayerJsonConverter());
        return settings;
    }

    /// <summary>Deep-snapshot <paramref name="source"/> to JSON with its identity fields stripped.
    /// Returns null for a null source.</summary>
    public static string Capture(LoupedeckButton source)
    {
        if (source == null) return null;

        var jo = JObject.FromObject(source, Serializer);

        // Strip the target-owned identity fields so apply never moves them: TouchButton/
        // RotaryButton carry a positional "Index"; SimpleButton carries a physical hardware "Id".
        jo.Remove("Index");
        jo.Remove("Id");

        return jo.ToString(Formatting.None);
    }

    /// <summary>Apply a snapshot onto <paramref name="target"/> in place (keeps the target
    /// instance, its Index/Id and its ItemChanged subscription), then re-wire and refresh it.</summary>
    public static void Apply(string json, LoupedeckButton target)
    {
        if (target == null || json == null) return;

        JsonConvert.PopulateObject(json, target, Settings);

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

    /// <summary>True when a button/side-display currently holds no user configuration, so it can
    /// be overwritten without asking (issue #166 empty-vs-modified rule).</summary>
    public static bool IsEmpty(LoupedeckButton button)
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
