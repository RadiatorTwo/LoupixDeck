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

    // The images decoded for the entries handed out last, and for the build before. A folder render
    // that read the previous entries may still be drawing them while a plugin announces new ones,
    // so a build frees the images of the build before last. Guarded by the render gate.
    private List<SKBitmap> _currentImages = [];
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

    public IReadOnlyDictionary<int, CoreFolder.RotaryOverride> RotaryOverrides =>
        _inner.RotaryOverrides.ToDictionary(
            kv => kv.Key,
            kv => new CoreFolder.RotaryOverride
            {
                OnLeft = kv.Value.OnLeft,
                OnRight = kv.Value.OnRight,
                OnPress = kv.Value.OnPress,
                Label = kv.Value.Label,
                GetValue = kv.Value.GetValue
            });

    public Sdk.ISideStripSession CreateSideStripSession(Sdk.SideStripContext context) =>
        _inner is Sdk.IFolderSideStripProvider strips ? strips.CreateSideStripSession(context) : null;

    public IReadOnlyList<CoreFolder.FolderEntry> BuildEntries()
    {
        long probe = Diagnostics.FolderRedrawProbe.Begin();
        var entries = _inner.BuildEntries() ?? Array.Empty<Sdk.FolderEntry>();
        Diagnostics.FolderRedrawProbe.End("  plugin build", probe);

        probe = Diagnostics.FolderRedrawProbe.Begin();
        List<CoreFolder.FolderEntry> converted = entries.Select(ConvertEntry).ToList();
        Diagnostics.FolderRedrawProbe.End("  png decode", probe);
        RetireImages(converted.Select(static e => e.Image).Where(static image => image != null).ToList());
        return converted;
    }

    /// <summary>Makes <paramref name="next"/> the current images and frees the build before last.</summary>
    private void RetireImages(List<SKBitmap> next)
    {
        lock (SkiaRenderGate.Sync)
        {
            foreach (SKBitmap image in _previousImages)
                image.Dispose();

            _previousImages = _currentImages;
            _currentImages = next;
        }
    }

    private static CoreFolder.FolderEntry ConvertEntry(Sdk.FolderEntry e)
    {
        return new CoreFolder.FolderEntry
        {
            SlotIndex = e.SlotIndex,
            Text = e.Text,
            Image = DecodeImage(e.Image),
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
