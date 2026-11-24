namespace LoupixDeck.Services.TextPaste;

/// <summary>
/// Inserts text through the clipboard instead of typing it: the text is put on the clipboard,
/// the paste shortcut is sent, and the clipboard content from before is put back afterwards.
/// Unlike typing, the result does not depend on how fast the target application reads its
/// keyboard input (the Windows 11 Notepad drops and mixes up typed characters, issue #691).
/// </summary>
public interface ITextPaster
{
    /// <summary>The shortcut used when no other is given: Ctrl+V, or Cmd+V on macOS.</summary>
    const string DefaultPasteKeys = "CtrlOrCmd+V";

    /// <summary>
    /// Pastes <paramref name="text"/> into the focused application.
    /// </summary>
    /// <param name="text">Text to insert. Line breaks are converted to the platform convention.</param>
    /// <param name="pasteKeys">
    /// Shortcut that triggers the paste, '+'-joined like a key combination ("Ctrl+Shift+V" for a
    /// Linux terminal). Null or blank uses <see cref="DefaultPasteKeys"/>.
    /// </param>
    /// <param name="token">Cancels a paste that has not started yet. Once the clipboard has been
    /// changed, the previous content is always restored.</param>
    /// <returns>False when the clipboard could not be set; nothing was pasted then.</returns>
    Task<bool> PasteAsync(string text, string pasteKeys, CancellationToken token = default);
}
