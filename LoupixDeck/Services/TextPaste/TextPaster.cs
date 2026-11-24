using LoupixDeck.Utils;

namespace LoupixDeck.Services.TextPaste;

/// <inheritdoc />
public sealed class TextPaster(IUInputKeyboard keyboard) : ITextPaster
{
    /// <summary>
    /// How long the pasted text stays on the clipboard before the previous content returns.
    /// The paste shortcut is handled asynchronously: the target reads the clipboard only when
    /// it processes the key, so restoring too early would paste the old content instead.
    /// </summary>
    private const int RestoreDelayMs = 500;

    // One paste at a time across all devices: a second paste starting while the first is still
    // waiting would capture the first one's text as "previous content" and restore that.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IPasteClipboard _clipboard = OperatingSystem.IsWindows()
        ? new WindowsPasteClipboard()
        : new AvaloniaPasteClipboard();

    public async Task<bool> PasteAsync(string text, string pasteKeys, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        IReadOnlyList<string> keys = KeyNames.SplitCombination(
            string.IsNullOrWhiteSpace(pasteKeys) ? ITextPaster.DefaultPasteKeys : pasteKeys);
        if (keys.Count == 0)
            return false;

        await Gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();

            object snapshot = await _clipboard.CaptureAsync();
            if (!await _clipboard.SetTextAsync(NormalizeLineBreaks(text)))
            {
                Console.Error.WriteLine("[TextPaster] Could not put the text on the clipboard.");
                return false;
            }

            try
            {
                keyboard.SendKeyCombination(keys);

                // Not cancellable: the previous content must come back even when the macro
                // is stopped right now.
                await Task.Delay(RestoreDelayMs, CancellationToken.None);
            }
            finally
            {
                // Leave the clipboard alone when the user copied something in the meantime.
                if ((snapshot != null) && await _clipboard.StillHoldsPastedTextAsync())
                    await _clipboard.RestoreAsync(snapshot);
            }

            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    // Editors store "\n"; Windows applications expect "\r\n" from the clipboard.
    private static string NormalizeLineBreaks(string text)
    {
        string unified = text.Replace("\r\n", "\n");
        return OperatingSystem.IsWindows() ? unified.Replace("\n", "\r\n") : unified;
    }
}
