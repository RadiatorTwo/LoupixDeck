using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Placeholder row of the layer list on a split wheel's all-segments surface: it stands for the
/// segments themselves. Layers listed above it are drawn over the segments, layers below it
/// behind them. Never part of a canvas and never persisted.
/// </summary>
public sealed class WheelSegmentsMarker : LayerBase
{
    public override string LayerKind => "wheel-segments-marker";
}

/// <summary>
/// The layer list of a split wheel's all-segments surface, topmost first: the layers in front of
/// the segments, the <see cref="WheelSegmentsMarker"/>, then the layers behind them
/// (<see cref="LayerBase.BehindSegments"/>). Follows the canvas live, and applies every change as
/// single moves, inserts and removes, so the list keeps its selection.
/// </summary>
public sealed class SharedSurfaceLayerView : ObservableCollection<LayerBase>, IDisposable
{
    private readonly ObservableCollection<LayerBase> _source;
    private readonly WheelSegmentsMarker _marker;
    private readonly HashSet<LayerBase> _watched = [];

    public SharedSurfaceLayerView(ObservableCollection<LayerBase> source, WheelSegmentsMarker marker)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _marker = marker ?? throw new ArgumentNullException(nameof(marker));
        _source.CollectionChanged += OnSourceChanged;
        Sync();
    }

    private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void OnLayerChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerBase.BehindSegments))
            Sync();
    }

    private void Sync()
    {
        // Watch exactly the layers on the canvas for a change of side.
        foreach (var gone in _watched.Where(l => !_source.Contains(l)).ToArray())
        {
            gone.PropertyChanged -= OnLayerChanged;
            _watched.Remove(gone);
        }

        foreach (var layer in _source)
        {
            if (layer != null && _watched.Add(layer))
                layer.PropertyChanged += OnLayerChanged;
        }

        // The paint order is the canvas order, first at the bottom, within each side.
        var front = _source.Where(l => l is { BehindSegments: false }).Reverse();
        var behind = _source.Where(l => l is { BehindSegments: true }).Reverse();
        List<LayerBase> desired = [.. front, _marker, .. behind];

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < Count && ReferenceEquals(this[i], desired[i])) continue;

            int at = IndexOf(desired[i]);
            if (at >= 0)
                Move(at, i);
            else
                Insert(i, desired[i]);
        }

        while (Count > desired.Count)
            RemoveAt(Count - 1);
    }

    public void Dispose()
    {
        _source.CollectionChanged -= OnSourceChanged;
        foreach (var layer in _watched)
            layer.PropertyChanged -= OnLayerChanged;
        _watched.Clear();
    }
}
