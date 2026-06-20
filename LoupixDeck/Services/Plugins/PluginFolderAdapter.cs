using Avalonia.Media;
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

    public PluginFolderAdapter(Sdk.IFolderProvider inner)
    {
        _inner = inner;
        _inner.EntriesChanged += () => EntriesChanged?.Invoke();
    }

    public string Title => _inner.Title;

    public event Action EntriesChanged;

    public void OnEnter() => _inner.OnEnter();

    public void OnExit() => _inner.OnExit();

    public IReadOnlyDictionary<int, CoreFolder.RotaryOverride> RotaryOverrides =>
        _inner.RotaryOverrides.ToDictionary(
            kv => kv.Key,
            kv => new CoreFolder.RotaryOverride
            {
                OnLeft = kv.Value.OnLeft,
                OnRight = kv.Value.OnRight,
                OnPress = kv.Value.OnPress
            });

    public IReadOnlyList<CoreFolder.FolderEntry> BuildEntries()
    {
        var entries = _inner.BuildEntries() ?? Array.Empty<Sdk.FolderEntry>();
        return entries.Select(ConvertEntry).ToList();
    }

    private static CoreFolder.FolderEntry ConvertEntry(Sdk.FolderEntry e)
    {
        return new CoreFolder.FolderEntry
        {
            SlotIndex = e.SlotIndex,
            Text = e.Text,
            Image = DecodeImage(e.Image),
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
            return SKBitmap.Decode(png);
        }
        catch
        {
            return null;
        }
    }
}
