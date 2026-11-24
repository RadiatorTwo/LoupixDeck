namespace LoupixDeck.Services.TextPaste;

/// <summary>
/// The platform clipboard as <see cref="TextPaster"/> needs it: take a snapshot, replace the
/// content with text, tell whether that text is still there, and put the snapshot back.
/// </summary>
internal interface IPasteClipboard
{
    /// <summary>Captures the current content. Null when the clipboard could not be read.</summary>
    Task<object> CaptureAsync();

    /// <summary>Replaces the content with <paramref name="text"/>. False on failure.</summary>
    Task<bool> SetTextAsync(string text);

    /// <summary>True while the clipboard still holds what <see cref="SetTextAsync"/> put there,
    /// i.e. nobody copied something else in the meantime.</summary>
    Task<bool> StillHoldsPastedTextAsync();

    /// <summary>Puts a snapshot from <see cref="CaptureAsync"/> back.</summary>
    Task RestoreAsync(object snapshot);
}
