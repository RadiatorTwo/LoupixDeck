using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.Portable;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

/// <summary>
/// Deep copies a touch or rotary page so it can sit next to its source in the same workspace
/// (issue #563). The copy goes through the config's JSON format and gets fresh ids, the same way
/// an imported page does.
/// </summary>
public static class PageCloner
{
    /// <summary>Copies <paramref name="source"/> with new page, state and layer ids, padded and
    /// wired for a device with <paramref name="touchButtonCount"/> keys.</summary>
    public static TouchButtonPage Clone(TouchButtonPage source, int touchButtonCount)
    {
        TouchButtonPage copy = CopyOf(source);
        PortablePayloadNormalizer.Normalize(copy, touchButtonCount);
        return copy;
    }

    /// <summary>Copies <paramref name="source"/> with new ids, padded to the device's dial counts.</summary>
    public static RotaryButtonPage Clone(RotaryButtonPage source, int rotaryButtonCount, int sideRotaryButtonCount)
    {
        RotaryButtonPage copy = CopyOf(source);
        PortablePayloadNormalizer.Normalize(copy, rotaryButtonCount, sideRotaryButtonCount);
        foreach (RotaryButton button in copy.RotaryButtons)
            button?.RewireAfterLoad();
        return copy;
    }

    /// <summary>Builds a touch page with new ids from a clipboard snapshot (issue #621), keeping its name.</summary>
    public static TouchButtonPage FromSnapshot(string json, int touchButtonCount)
    {
        TouchButtonPage page = Restore<TouchButtonPage>(JObject.Parse(json));
        PortablePayloadNormalizer.Normalize(page, touchButtonCount);
        return page;
    }

    /// <summary>Builds a rotary page for <paramref name="side"/> with new ids from a clipboard
    /// snapshot (issue #621), keeping its name.</summary>
    public static RotaryButtonPage FromSnapshot(string json, RotarySide side, int rotaryButtonCount, int sideRotaryButtonCount)
    {
        RotaryButtonPage page = Restore<RotaryButtonPage>(JObject.Parse(json));
        page.Side = side;
        PortablePayloadNormalizer.Normalize(page, rotaryButtonCount, sideRotaryButtonCount);
        foreach (RotaryButton button in page.RotaryButtons)
            button?.RewireAfterLoad();
        return page;
    }

    private static T CopyOf<T>(T source) where T : ButtonPageBase
    {
        ArgumentNullException.ThrowIfNull(source);

        T copy = Restore<T>(JObject.FromObject(source, ButtonSnapshot.Serializer));
        copy.Name = string.IsNullOrWhiteSpace(source.Name)
            ? string.Empty
            : Loc.Tr("Page_CopyName", source.Name);
        return copy;
    }

    private static T Restore<T>(JObject json) where T : ButtonPageBase
    {
        // Remap gives every "Id" in the page a new value and rewrites the ids embedded in command
        // strings and layer owner keys, so the copy's states and layers are its own. Folder ids are
        // not part of the page, so links to the workspace's folders keep working.
        (JObject remapped, _) = PortableIdRemapper.Remap(json);
        return remapped.ToObject<T>(ButtonSnapshot.Serializer);
    }
}
