using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// The app's own window frame: a 32px title bar, the caption buttons and the resize edges,
/// wrapped around whatever the window puts inside it. Used by windows that carry
/// <c>Classes="app-chrome"</c> (see Views/Styles/WindowChromeStyles.axaml), which turns the
/// platform decorations off.
///
/// Everything interactive is declared in the template through
/// <see cref="Avalonia.Controls.Chrome.WindowDecorationProperties"/> element roles rather than
/// with pointer handlers, so the platform keeps doing the work it is good at: window snapping,
/// the resize cursors, and the Windows snap-layout flyout on the maximise button.
///
/// A <see cref="TemplatedControl"/> rather than a UserControl on purpose: a UserControl's XAML
/// body *is* its Content, so a window putting its own content inside would replace the frame
/// instead of being wrapped by it.
/// </summary>
public class AppWindowFrame : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AppWindowFrame, string>(nameof(Title), string.Empty);

    /// <summary>Hidden by default: the frame is used by modal dialogs first, and a minimised
    /// modal window is a broken state on X11.</summary>
    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<AppWindowFrame, bool>(nameof(ShowMinimize));

    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<AppWindowFrame, bool>(nameof(ShowMaximize), true);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowMinimize
    {
        get => GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }
}
