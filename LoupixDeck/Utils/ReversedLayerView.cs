using System.Collections.ObjectModel;
using System.Collections.Specialized;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.Utils;

/// <summary>
/// A live, reversed mirror of a button's layer collection for the editor's layer list. The
/// model keeps the first layer at the bottom (the renderer paints in order), while the list
/// should read like every other layer panel: the topmost layer first. Each source change is
/// mapped onto the mirrored index, so the list sees the same add, remove, move and replace
/// events it would see on the model, and its selection behaves the same.
/// </summary>
public sealed class ReversedLayerView : ObservableCollection<LayerBase>, IDisposable
{
    private readonly ObservableCollection<LayerBase> _source;

    public ReversedLayerView(ObservableCollection<LayerBase> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Rebuild();
        _source.CollectionChanged += OnSourceChanged;
    }

    private void Rebuild()
    {
        Clear();
        for (int i = _source.Count - 1; i >= 0; i--)
            Add(_source[i]);
    }

    private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        int n = _source.Count;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { Count: 1 }:
                // The source already holds the item: index n-1 maps to the top of the list.
                Insert(n - 1 - e.NewStartingIndex, (LayerBase)e.NewItems[0]);
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is { Count: 1 }:
                // The source had n+1 items when the removed one sat at its old index.
                RemoveAt(n - e.OldStartingIndex);
                break;

            case NotifyCollectionChangedAction.Move when e.NewItems is { Count: 1 }:
                Move(n - 1 - e.OldStartingIndex, n - 1 - e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Replace when e.NewItems is { Count: 1 }:
                this[n - 1 - e.NewStartingIndex] = (LayerBase)e.NewItems[0];
                break;

            default:
                Rebuild();
                break;
        }
    }

    public void Dispose() => _source.CollectionChanged -= OnSourceChanged;
}
