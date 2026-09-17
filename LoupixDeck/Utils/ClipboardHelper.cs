using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace LoupixDeck.Utils;

/// <summary>
/// Writes text to the system clipboard. Avalonia exposes the clipboard only through a TopLevel,
/// so this resolves a window the same way the file pickers do - the active one, because the
/// window asking to copy is usually a modal dialog rather than the main window.
/// </summary>
public static class ClipboardHelper
{
    /// <summary>Copies <paramref name="text"/> to the clipboard. False when that was not possible.</summary>
    public static async Task<bool> SetTextAsync(string text)
    {
        Window owner = WindowHelper.GetActiveWindow() ?? WindowHelper.GetMainWindow();
        IClipboard clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;

        if (clipboard == null)
        {
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Clipboard] Could not copy to the clipboard: {ex.Message}");
            return false;
        }
    }
}
