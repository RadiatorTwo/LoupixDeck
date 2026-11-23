using LoupixDeck.Models.Layers;
using Newtonsoft.Json;

namespace LoupixDeck.Services;

/// <summary>
/// In-app clipboard for copy/paste of layers in the button editor (issues #583, #679). Holds a
/// JSON <b>snapshot</b> of the copied layers, so it survives closing the editor and later edits of
/// the sources, and a paste lands on any button, state, wheel or side display. Registered as a root
/// singleton and forwarded into every device provider, so it works across buttons and devices.
/// </summary>
public interface ILayerClipboardService
{
    /// <summary>The kinds of the copied layers (<see cref="LayerBase.LayerKind"/>) in paint order; empty when nothing is copied.</summary>
    IReadOnlyList<string> CurrentKinds { get; }

    /// <summary>Take a deep snapshot of <paramref name="layers"/>, kept in the given order.</summary>
    void Copy(IReadOnlyList<LayerBase> layers);

    /// <summary>
    /// New, independent layers built from the snapshot in the copied order, or an empty list when
    /// nothing is copied. Each gets a fresh id and is a plain user layer: the owner of a
    /// command-owned source does not travel.
    /// </summary>
    IReadOnlyList<LayerBase> CreateCopies();
}

public sealed class LayerClipboardService : ILayerClipboardService
{
    private string _json;

    public IReadOnlyList<string> CurrentKinds { get; private set; } = [];

    public void Copy(IReadOnlyList<LayerBase> layers)
    {
        List<LayerBase> copied = layers?.Where(l => l != null).ToList() ?? [];
        if (copied.Count == 0) return;

        _json = JsonConvert.SerializeObject(copied, typeof(List<LayerBase>), ButtonSnapshot.Settings);
        CurrentKinds = copied.Select(l => l.LayerKind).ToList();
    }

    public IReadOnlyList<LayerBase> CreateCopies()
    {
        if (_json == null) return [];

        List<LayerBase> layers;
        try
        {
            layers = JsonConvert.DeserializeObject<List<LayerBase>>(_json, ButtonSnapshot.Settings);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[LayerClipboard] Paste failed: {ex.Message}");
            return [];
        }

        if (layers == null) return [];

        layers.RemoveAll(l => l == null);
        foreach (LayerBase layer in layers)
        {
            layer.Id = Guid.NewGuid();
            layer.OwnerKey = null;
            layer.CommandName = null;
            layer.OwnerCreated = false;
            layer.BehindSegments = false;
            // A pasted copy is the user's own layer, not one the zone-divider option removes again.
            if (layer is SymbolLayer symbol)
                symbol.IsZoneDivider = false;
        }

        return layers;
    }
}
