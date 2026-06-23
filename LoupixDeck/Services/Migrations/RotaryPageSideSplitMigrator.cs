using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v3 → v4: splits the single <c>RotaryButtonPages</c> list into
/// independent <c>LeftRotaryButtonPages</c> / <c>RightRotaryButtonPages</c> sets so
/// devices with side strips (Razer Stream Controller) can page each dial column on
/// its own.
/// </summary>
/// <remarks>
/// Only Razer configs (6 knobs, 3 per side) are split: each original page becomes a
/// left page (knobs 0–2) and a right page (knobs 3–5, re-indexed 0–2), with the
/// command wraps copied to both sides. Devices without side strips (Live S) keep
/// their single <c>RotaryButtonPages</c> list untouched — only the version bumps.
/// </remarks>
public sealed class RotaryPageSideSplitMigrator : IConfigMigration
{
    public int FromVersion => 3;

    // Razer Stream Controller USB product id.
    private const string RazerPid = "0d06";

    public void Apply(JObject root, string configFilePath)
    {
        if (IsSideStripDevice(root))
            SplitRotaryPages(root);

        root["Version"] = FromVersion + 1;
    }

    /// <summary>
    /// True when the config belongs to a device with side strips. Primary signal is
    /// the persisted product id; as a fallback (very old configs may lack it) any
    /// rotary page carrying more than two knobs identifies the 6-knob Razer.
    /// </summary>
    private static bool IsSideStripDevice(JObject root)
    {
        var pid = root["DevicePid"]?.Value<string>();
        if (string.Equals(pid, RazerPid, StringComparison.OrdinalIgnoreCase))
            return true;

        var pages = root["RotaryButtonPages"] as JArray;
        var firstButtons = (pages?.FirstOrDefault() as JObject)?["RotaryButtons"] as JArray;
        return firstButtons is { Count: > 2 };
    }

    private static void SplitRotaryPages(JObject root)
    {
        var pages = root["RotaryButtonPages"] as JArray ?? new JArray();

        var left = new JArray();
        var right = new JArray();

        var pageNumber = 1;
        foreach (var page in pages.OfType<JObject>())
        {
            left.Add(BuildSidePage(page, pageNumber, sideValue: 1, keepIndices: i => i <= 2, indexOffset: 0));
            right.Add(BuildSidePage(page, pageNumber, sideValue: 2, keepIndices: i => i >= 3, indexOffset: 3));
            pageNumber++;
        }

        root["LeftRotaryButtonPages"] = left;
        root["RightRotaryButtonPages"] = right;
        // Single source of truth from now on: the Razer reads only the side lists.
        root["RotaryButtonPages"] = new JArray();
    }

    /// <summary>
    /// Clones a page, keeping only the knobs selected by <paramref name="keepIndices"/>
    /// and re-indexing them to a 0-based per-side range (subtracting
    /// <paramref name="indexOffset"/>). Command wraps and the name carry over.
    /// </summary>
    private static JObject BuildSidePage(JObject source, int pageNumber, int sideValue,
        Func<int, bool> keepIndices, int indexOffset)
    {
        var clone = (JObject)source.DeepClone();
        clone["Page"] = pageNumber;
        clone["Side"] = sideValue;

        var buttons = new JArray();
        if (clone["RotaryButtons"] is JArray src)
        {
            foreach (var btn in src.OfType<JObject>())
            {
                var index = btn["Index"]?.Value<int?>() ?? 0;
                if (!keepIndices(index)) continue;
                btn["Index"] = index - indexOffset;
                buttons.Add(btn);
            }
        }

        clone["RotaryButtons"] = buttons;
        return clone;
    }
}
