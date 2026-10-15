using System.IO.Hashing;
using Avalonia.Media;
using LoupixDeck.Utils;
using SkiaSharp;
using CoreFolder = LoupixDeck.Services.FolderNavigation;
using Sdk = LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Adapts a plugin's SDK <see cref="Sdk.IFolderProvider"/> to the core's folder
/// navigation contract, converting the UI-free SDK value types
/// (<see cref="Sdk.PluginColor"/>, PNG bytes) into the core's Avalonia/Skia types.
/// </summary>
internal sealed class PluginFolderAdapter : CoreFolder.IFolderProvider
{
    private readonly Sdk.IFolderProvider _inner;
    private readonly Lock _subscriptionLock = new();
    private Action _entriesChanged;

    // The images decoded for the entries handed out last, by a hash of their PNG bytes, and the ones
    // the build before used and the last one no longer does. A folder render that read the previous
    // entries may still be drawing them while a plugin announces new ones, so a build frees only the
    // images dropped one build earlier. An image whose bytes come again is reused, not decoded anew.
    // Guarded by the render gate.
    private Dictionary<ulong, SKBitmap> _currentImages = [];
    private List<SKBitmap> _previousImages = [];

    public PluginFolderAdapter(Sdk.IFolderProvider inner)
    {
        _inner = inner;
    }

    public string Title => _inner.Title;

    /// <summary>
    /// Forwards the plugin's event only while the core listens. A sub-folder adapter is created
    /// on every rebuild of its parent, so subscribing up front would pile one handler per rebuild
    /// onto a plugin that reuses its sub-folder instance, and keep each adapter alive with it.
    /// </summary>
    public event Action EntriesChanged
    {
        add
        {
            lock (_subscriptionLock)
            {
                bool first = _entriesChanged == null;
                _entriesChanged += value;
                if (first && _entriesChanged != null)
                    _inner.EntriesChanged += OnInnerEntriesChanged;
            }
        }
        remove
        {
            lock (_subscriptionLock)
            {
                if (_entriesChanged == null)
                    return;

                _entriesChanged -= value;
                if (_entriesChanged == null)
                    _inner.EntriesChanged -= OnInnerEntriesChanged;
            }
        }
    }

    private void OnInnerEntriesChanged() => Volatile.Read(ref _entriesChanged)?.Invoke();

    public void OnEnter() => _inner.OnEnter();

    public void OnExit()
    {
        try
        {
            _inner.OnExit();
        }
        finally
        {
            // Nothing renders this folder any more, apart from a render still in flight.
            RetireImages([]);
        }
    }

    /// <summary>
    /// The plugin's dial overrides as core types. Read on every folder redraw and every dial turn
    /// or press, so the conversion is kept and redone only when the plugin hands out different
    /// overrides: SDK overrides are init-only, so the same objects under the same keys convert to
    /// the same result.
    /// </summary>
    public IReadOnlyDictionary<int, CoreFolder.RotaryOverride> RotaryOverrides
    {
        get
        {
            IReadOnlyDictionary<int, Sdk.RotaryOverride> source = _inner.RotaryOverrides;
            lock (_overridesLock)
            {
                if (_overrides != null && SameOverrides(source))
                    return _overrides;

                _overrideSources = source?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
                _overrides = _overrideSources.ToDictionary(
                    kv => kv.Key,
                    kv => new CoreFolder.RotaryOverride
                    {
                        OnLeft = kv.Value.OnLeft,
                        OnRight = kv.Value.OnRight,
                        OnPress = kv.Value.OnPress,
                        Label = kv.Value.Label,
                        GetValue = kv.Value.GetValue
                    });
                return _overrides;
            }
        }
    }

    private readonly Lock _overridesLock = new();
    private Dictionary<int, Sdk.RotaryOverride> _overrideSources;
    private Dictionary<int, CoreFolder.RotaryOverride> _overrides;

    /// <summary>True when <paramref name="source"/> holds exactly the overrides last converted.</summary>
    private bool SameOverrides(IReadOnlyDictionary<int, Sdk.RotaryOverride> source)
    {
        if (source == null) return _overrideSources.Count == 0;
        if (source.Count != _overrideSources.Count) return false;

        foreach (KeyValuePair<int, Sdk.RotaryOverride> kv in source)
        {
            if (!_overrideSources.TryGetValue(kv.Key, out Sdk.RotaryOverride known) ||
                !ReferenceEquals(known, kv.Value))
                return false;
        }

        return true;
    }

    public Sdk.ISideStripSession CreateSideStripSession(Sdk.SideStripContext context) =>
        _inner is Sdk.IFolderSideStripProvider strips ? strips.CreateSideStripSession(context) : null;

    public IReadOnlyList<CoreFolder.FolderEntry> BuildEntries()
    {
        long probe = Diagnostics.FolderRedrawProbe.Begin();
        var entries = _inner.BuildEntries() ?? Array.Empty<Sdk.FolderEntry>();
        Diagnostics.FolderRedrawProbe.End("  plugin build", probe);

        probe = Diagnostics.FolderRedrawProbe.Begin();
        var images = new Dictionary<ulong, SKBitmap>();
        List<CoreFolder.FolderEntry> converted = entries.Select(e => ConvertEntry(e, images)).ToList();
        Diagnostics.FolderRedrawProbe.End("  png decode", probe);
        RetireImages(images);
        return converted;
    }

    /// <summary>
    /// Makes <paramref name="next"/> the current images, frees the ones dropped by the build before,
    /// and holds back the current ones <paramref name="next"/> no longer uses for one more build.
    /// </summary>
    private void RetireImages(Dictionary<ulong, SKBitmap> next)
    {
        lock (SkiaRenderGate.Sync)
        {
            // A build overlapping an exit may have picked an image up again after it was set aside.
            var kept = new HashSet<SKBitmap>(next.Values, ReferenceEqualityComparer.Instance);
            foreach (SKBitmap image in _previousImages)
            {
                if (!kept.Contains(image))
                    image.Dispose();
            }

            _previousImages = _currentImages.Values.Where(image => !kept.Contains(image)).ToList();
            _currentImages = next;
        }
    }

    /// <summary>
    /// The decoded image for <paramref name="png"/>: the one this build or the last one already
    /// decoded from the same bytes, or a new decode. Recorded in <paramref name="images"/>.
    /// </summary>
    private SKBitmap DecodeOrReuse(byte[] png, Dictionary<ulong, SKBitmap> images)
    {
        if (png == null || png.Length == 0)
            return null;

        ulong hash = XxHash3.HashToUInt64(png);
        if (images.TryGetValue(hash, out SKBitmap image))
            return image;

        lock (SkiaRenderGate.Sync)
        {
            _currentImages.TryGetValue(hash, out image);
        }

        image ??= DecodeImage(png);
        if (image != null)
            images[hash] = image;
        return image;
    }

    private CoreFolder.FolderEntry ConvertEntry(Sdk.FolderEntry e, Dictionary<ulong, SKBitmap> images)
    {
        return new CoreFolder.FolderEntry
        {
            SlotIndex = e.SlotIndex,
            Text = e.Text,
            Image = DecodeOrReuse(e.Image, images),
            Render = e.Render,
            BackColor = ToColor(e.BackColor),
            TextColor = ToColor(e.TextColor),
            TextSize = e.TextSize,
            Bold = e.Bold,
            OnPress = e.OnPress,
            OpensFolder = e.OpensFolder != null ? new PluginFolderAdapter(e.OpensFolder) : null
        };
    }

    private static Color ToColor(Sdk.PluginColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static SKBitmap DecodeImage(byte[] png)
    {
        if (png == null || png.Length == 0)
            return null;

        try
        {
            // Only ever drawn from, so immutable: Skia then does not copy it on every draw.
            SKBitmap image = SKBitmap.Decode(png);
            image?.SetImmutable();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
