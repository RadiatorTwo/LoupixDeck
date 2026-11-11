using LoupixDeck.Models.Layers;
using Newtonsoft.Json;

namespace LoupixDeck.Services;

/// <summary>
/// In-app clipboard for copy/paste of single layers in the button editor (issue #583). Holds a
/// JSON <b>snapshot</b> of the copied layer, so it survives closing the editor and later edits of
/// the source, and a paste lands on any button, state, wheel or side display. Registered as a root
/// singleton and forwarded into every device provider, so it works across buttons and devices.
/// </summary>
public interface ILayerClipboardService
{
    /// <summary>The kind of the copied layer (<see cref="LayerBase.LayerKind"/>), or null when empty.</summary>
    string CurrentKind { get; }

    /// <summary>Take a deep snapshot of <paramref name="layer"/>.</summary>
    void Copy(LayerBase layer);

    /// <summary>
    /// A new, independent layer built from the snapshot, or null when nothing is copied. It gets a
    /// fresh id and is a plain user layer: the owner of a command-owned source does not travel.
    /// </summary>
    LayerBase CreateCopy();
}

public sealed class LayerClipboardService : ILayerClipboardService
{
    private string _json;

    public string CurrentKind { get; private set; }

    public void Copy(LayerBase layer)
    {
        if (layer == null) return;
        _json = JsonConvert.SerializeObject(layer, typeof(LayerBase), ButtonSnapshot.Settings);
        CurrentKind = layer.LayerKind;
    }

    public LayerBase CreateCopy()
    {
        if (_json == null) return null;

        LayerBase layer;
        try
        {
            layer = JsonConvert.DeserializeObject<LayerBase>(_json, ButtonSnapshot.Settings);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[LayerClipboard] Paste failed: {ex.Message}");
            return null;
        }

        if (layer == null) return null;

        layer.Id = Guid.NewGuid();
        layer.OwnerKey = null;
        layer.CommandName = null;
        layer.OwnerCreated = false;
        layer.BehindSegments = false;
        return layer;
    }
}
