using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoupixDeck.Localization;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// The app's own 32px title bar, used by windows that carry <c>Classes="app-chrome"</c>
/// (see Views/Styles/WindowChromeStyles.axaml). The OS caption is switched off there, so
/// dragging, maximising and closing happen here. Nothing about this control is specific to
/// one window: it resolves its host through <see cref="TopLevel.GetTopLevel"/>, so any
/// window can adopt it.
/// </summary>
public partial class AppTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AppTitleBar, string>(nameof(Title), string.Empty);

    /// <summary>Hidden by default: the control is used by modal dialogs first, and a
    /// minimised modal window is a broken state on X11.</summary>
    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<AppTitleBar, bool>(nameof(ShowMinimize));

    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<AppTitleBar, bool>(nameof(ShowMaximize), true);

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

    public AppTitleBar()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Host is { } window)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdateMaximizeButton();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Host is { } window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private Window Host => TopLevel.GetTopLevel(this) as Window;

    private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeButton();
        }
    }

    /// <summary>The one button whose glyph and tooltip depend on the window state.</summary>
    private void UpdateMaximizeButton()
    {
        if (MaximizeButton == null)
        {
            return;
        }

        bool maximized = Host?.WindowState == WindowState.Maximized;
        // mdi-window-restore / mdi-window-maximize
        MaximizeButton.Content = maximized ? "\uF05B2" : "\uF05AF";
        ToolTip.SetTip(MaximizeButton, Loc.Tr(maximized ? "Window_Restore" : "Window_Maximize"));
    }

    private void OnBarPressed(object sender, PointerPressedEventArgs e)
    {
        if (Host is not { } window || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (Host is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (Host is not { } window || !ShowMaximize)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Host?.Close();
}
