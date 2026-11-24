using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.TextPaste;

/// <summary>
/// Clipboard for <see cref="TextPaster"/> on Linux and macOS, through Avalonia. Avalonia only
/// reaches the clipboard through a window, and keeps serving it from that window, so the main
/// window is used even while it is hidden in the tray. The snapshot keeps the most useful of
/// text, files or an image; other formats are lost when the clipboard is restored.
/// </summary>
internal sealed class AvaloniaPasteClipboard : IPasteClipboard
{
    private sealed record Snapshot(string Text, IStorageItem[] Files, Bitmap Image);

    private string _pastedText;

    public Task<object> CaptureAsync() => OnUiThread<object>(async clipboard =>
    {
        if (await clipboard.TryGetTextAsync() is { } text)
            return new Snapshot(text, null, null);
        if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files)
            return new Snapshot(null, files, null);
        if (await clipboard.TryGetBitmapAsync() is { } image)
            return new Snapshot(null, null, image);
        return new Snapshot(null, null, null);
    });

    public async Task<bool> SetTextAsync(string text)
    {
        _pastedText = text;
        return await OnUiThread(async clipboard =>
        {
            await clipboard.SetTextAsync(text);
            return true;
        });
    }

    public Task<bool> StillHoldsPastedTextAsync() => OnUiThread(async clipboard =>
        await clipboard.TryGetTextAsync() == _pastedText);

    public Task RestoreAsync(object snapshot) => OnUiThread(async clipboard =>
    {
        switch ((Snapshot)snapshot)
        {
            case { Text: { } text }:
                await clipboard.SetTextAsync(text);
                break;
            case { Files: { } files }:
                await clipboard.SetFilesAsync(files);
                break;
            case { Image: { } image }:
                await clipboard.SetBitmapAsync(image);
                break;
            default:
                await clipboard.ClearAsync();
                break;
        }
        return true;
    });

    private static async Task<T> OnUiThread<T>(Func<IClipboard, Task<T>> action)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IClipboard clipboard = TopLevel.GetTopLevel(WindowHelper.GetMainWindow())?.Clipboard;
                if (clipboard == null)
                {
                    Console.Error.WriteLine("[TextPaster] No window to reach the clipboard through.");
                    return default;
                }

                return await action(clipboard);
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TextPaster] Clipboard access failed: {ex.Message}");
            return default;
        }
    }
}
