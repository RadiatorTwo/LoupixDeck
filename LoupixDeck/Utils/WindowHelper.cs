using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace LoupixDeck.Utils;

public static class WindowHelper
{
    public static Window GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    /// <summary>
    /// The window a picker or dialog should be parented to: the currently active one, falling back
    /// to the main window. Parenting a file picker to the main window while a modal dialog (the
    /// settings window) is open leaves the picker behind that dialog.
    /// </summary>
    public static Window GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
    }
}