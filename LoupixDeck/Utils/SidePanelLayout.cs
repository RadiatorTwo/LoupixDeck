using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LoupixDeck.Utils;

/// <summary>
/// Opens and closes the side panels inside the main window: the apps and commands panel on the
/// left of the deck, the folder panel on the right.
/// </summary>
/// <remarks>
/// A panel slides out like a drawer while the window grows by its width on the panel's own side,
/// so the deck stays where it is on screen: the left panel moves the window's left edge out, the
/// right panel its right edge. When the screen has no room on that side the window grows the other
/// way, and when it has no room at all the deck zooms out, animated like any zoom, to make room. A
/// closing panel gives all of it back.
///
/// While a panel moves the window is held at a fixed size and every frame sets it by hand; a window
/// that sized itself to its content goes back to doing so once nothing moves.
/// </remarks>
public sealed class SidePanelLayout
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);
    private static readonly CubicEaseOut Easing = new();

    private readonly Window _window;
    private readonly ScrollViewer _viewport;
    private readonly DeviceViewZoom _zoom;
    private readonly Func<double> _contentHeight;
    private readonly Side _left;
    private readonly Side _right;

    // Whether the window sized itself to its content before the panels pinned its size.
    private bool _restoreSizeToContent;
    private bool _pinned;
    private Window _cover;
    private readonly Canvas _coverCanvas = new() { Opacity = 0 };
    private double _baseWidth;
    private int _baseX;

    /// <param name="contentHeight">
    /// The height the window's content needs. A window that sizes itself to its content follows it
    /// on every frame, so a deck zooming to make room is tracked as it happens rather than snapped
    /// to at the end.
    /// </param>
    public SidePanelLayout(Window window, ScrollViewer viewport, DeviceViewZoom zoom,
        Func<double> contentHeight,
        Control leftHost, double leftWidth, Control rightHost, double rightWidth)
    {
        _window = window;
        _viewport = viewport;
        _zoom = zoom;
        _contentHeight = contentHeight;
        if (OperatingSystem.IsWindows())
            _window.Opened += (_, _) => Cover();

        // Where the window moves and resizes in one step and no cover can be drawn (X11), its left
        // edge follows the frames like the right one does. LOUPIX_SIDEPANEL_EDGE=jump turns that off.
        bool edgeFollows = !OperatingSystem.IsWindows() && WindowBounds.CanSetLeftAndWidth(window) &&
            Environment.GetEnvironmentVariable("LOUPIX_SIDEPANEL_EDGE") != "jump";
        _left = new Side(leftHost, leftWidth, isLeft: true, edgeFollows);
        _right = new Side(rightHost, rightWidth, isLeft: false, edgeFollows);
    }

    public bool IsLeftOpen => _left.IsOpen;
    public bool IsRightOpen => _right.IsOpen;

    public void SetLeftOpen(bool open, bool animate = true) => SetOpen(_left, open, animate);
    public void SetRightOpen(bool open, bool animate = true) => SetOpen(_right, open, animate);

    private sealed class Side(Control host, double fullWidth, bool isLeft, bool edgeFollows)
    {
        public Control Host { get; } = host;
        public double FullWidth { get; } = fullWidth;
        public bool IsLeft { get; } = isLeft;
        public bool IsOpen { get; set; }

        // What this panel currently adds to the window, in logical units: the width it grew by, and
        // how far of that the left edge moved out.
        public double Grown { get; set; }
        public double Shifted { get; set; }

        /// <summary>How much of the panel shows, from 0 (closed) to 1 (open).</summary>
        public double Shown { get; set; }

        /// <summary>
        /// Whether the window's left edge moves for this panel. It then takes its new size in one
        /// step and only the panel slides, inside the room already made for it. Never where the
        /// edge follows the frames instead.
        /// </summary>
        public bool Jumps => !edgeFollows && (Shifted > 0 || ShiftTo > 0);

        public TranslateTransform Slide { get; } = new();

        // The running animation: start and target of what shows and of the window's share.
        public TimeSpan? Start { get; set; }
        public bool Running { get; set; }
        public double ShownFrom { get; set; }
        public double ShownTo { get; set; }
        public double GrowFrom { get; set; }
        public double GrowTo { get; set; }
        public double ShiftFrom { get; set; }
        public double ShiftTo { get; set; }

        public DeviceViewZoom.ZoomLoan Loan { get; set; }
    }

    private void SetOpen(Side side, bool open, bool animate)
    {
        if (side.IsOpen == open && (side.Running || side.Host.IsVisible == open)) return;
        side.IsOpen = open;

        side.ShownFrom = side.Shown;
        side.GrowFrom = side.Grown;
        side.ShiftFrom = side.Shifted;

        if (open)
        {
            side.ShownTo = 1;
            PlanOpening(side, side.FullWidth);
            if (side.Host is Decorator { Child: { } content })
                content.RenderTransform = side.Slide;
        }
        else
        {
            side.ShownTo = 0;
            side.GrowTo = 0;
            side.ShiftTo = 0;

            _zoom.Restore(side.Loan);
            side.Loan = null;
        }

        Pin();

        // A moving left edge cannot follow the frames: the window would show each new position
        // with the previous frame's content for a moment, and the deck would jitter. The window
        // takes its new size up front instead, and gives it back once the panel has slid away.
        if (open && side.Jumps)
        {
            side.Grown = side.GrowFrom = side.GrowTo;
            side.Shifted = side.ShiftFrom = side.ShiftTo;
            void Resize()
            {
                ShowHost(side);
                ApplyWindow();
            }

            // Drawn as an edge moving out, with the panel sliding out along with it, when all of the
            // room comes from the left; otherwise (the screen's left end in the way) the window
            // changes in one covered step and the panel slides out after it.
            int grow = _window.Position.X - TargetX();
            bool leftOnly = Math.Abs(side.GrowTo - side.ShiftTo) < 0.5;
            side.Running = true;
            bool edgeMoves = leftOnly && animate && OperatingSystem.IsWindows() &&
                WindowEdgeGrowth.TryRun(_window, Cover(), _coverCanvas, grow, opening: true, Duration, Easing,
                    Resize,
                    t =>
                    {
                        side.Shown = Lerp(side.ShownFrom, side.ShownTo, t);
                        Render(side);
                    },
                    () =>
                    {
                        side.Running = false;
                        side.Shown = side.ShownTo;
                        Render(side);
                        Finish(side);
                    });
            if (!edgeMoves)
            {
                side.Running = false;
                Freeze(TargetX(), _baseWidth + _left.Grown + _right.Grown, Resize, () => Begin(side, animate));
            }
            return;
        }

        // A fully open panel that gives back room on the left travels with the edge moving in.
        if (!open && animate && side.Jumps && !side.Running && side.Shown >= 1 && TryShrinkEdge(side))
            return;

        if (open) ShowHost(side);
        Begin(side, animate);
    }

    private static void ShowHost(Side side)
    {
        Render(side);
        side.Host.IsVisible = true;
    }

    private void Begin(Side side, bool animate)
    {
        if (!animate)
        {
            Step(side, 1.0);
            Finish(side);
            return;
        }

        side.Start = null;
        if (side.Running) return;

        side.Running = true;
        _window.RequestAnimationFrame(time => OnFrame(side, time));
    }

    /// <summary>
    /// Works out how much of the panel's width the window can grow by, and which way, and lets the
    /// deck zoom out for whatever the screen cannot give.
    /// </summary>
    private void PlanOpening(Side side, double full)
    {
        side.GrowTo = side.Grown;
        side.ShiftTo = side.Shifted;

        double wanted = full - side.Grown;
        if (wanted <= 0) return;

        double extra = 0;
        double leftRoom = 0;
        double rightRoom = 0;

        if (_window.WindowState == WindowState.Normal &&
            (_window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary) is { } screen)
        {
            double scale = _window.DesktopScaling;
            PixelRect work = screen.WorkingArea;
            PixelPoint position = _window.Position;
            double frame = (_window.FrameSize ?? _window.ClientSize).Width;

            leftRoom = Math.Max(0, (position.X - work.X) / scale);
            rightRoom = Math.Max(0, ((work.Right - position.X) / scale) - frame);

            extra = Math.Min(wanted, leftRoom + rightRoom);
        }

        // What the window cannot grow by comes out of the deck's room.
        double shortfall = wanted - extra;
        if (shortfall > 0.5 && side.Loan == null)
        {
            double room = _viewport.Bounds.Width - shortfall;
            side.Loan = _zoom.ShrinkToWidth(room);

            // The zoom is rounded down to a whole percent, so the deck ends up a little narrower than
            // the room made for it. A window sized to its content would snap to that at the end;
            // growing it by that much less lands on the same width.
            if (side.Loan != null && _window.SizeToContent == SizeToContent.WidthAndHeight)
                extra = Math.Max(0, extra - Math.Max(0, room - _zoom.ViewWidthAt(side.Loan.Applied)));
        }

        double extraShift = side.IsLeft ? Math.Min(extra, leftRoom) : Math.Max(0, extra - rightRoom);
        side.GrowTo = side.Grown + extra;
        side.ShiftTo = side.Shifted + extraShift;
    }

    private void OnFrame(Side side, TimeSpan time)
    {
        side.Start ??= time;
        double progress = Math.Clamp((time - side.Start.Value) / Duration, 0, 1);

        Step(side, Easing.Ease(progress));

        if (progress < 1)
        {
            _window.RequestAnimationFrame(t => OnFrame(side, t));
            return;
        }

        side.Running = false;
        Finish(side);
    }

    private void Step(Side side, double t)
    {
        side.Shown = Lerp(side.ShownFrom, side.ShownTo, t);
        Render(side);

        // A panel whose window jumped keeps it at its size until it has slid away (Finish).
        if (side.Jumps) return;

        side.Grown = Lerp(side.GrowFrom, side.GrowTo, t);
        side.Shifted = Lerp(side.ShiftFrom, side.ShiftTo, t);
        ApplyWindow();
    }

    /// <summary>
    /// Shows <see cref="Side.Shown"/> of the panel. One that grows its window narrows its column;
    /// one whose window jumped has its whole column from the start and slides in from the edge.
    /// </summary>
    private static void Render(Side side)
    {
        double hidden = (1 - side.Shown) * side.FullWidth;

        if (side.Jumps)
        {
            side.Host.Width = side.FullWidth;
            // Out from the deck's side towards the window edge.
            side.Slide.X = side.IsLeft ? hidden : -hidden;
        }
        else
        {
            side.Host.Width = side.Shown * side.FullWidth;
            side.Slide.X = 0;
        }
    }

    /// <summary>
    /// Runs <paramref name="apply"/>, which moves the window's left edge, under a still picture of
    /// the screen, and then <paramref name="then"/>.
    /// </summary>
    /// <remarks>
    /// A window whose left edge moves shows one frame at its new position with its previous
    /// content at its previous size: the deck jumps aside and the desktop shows through beside it.
    /// No ordering of the calls avoids that frame, so it is covered instead. A picture of the area
    /// the window covers before, during and after the change is laid over it in a window of its
    /// own, the change happens underneath, and the picture goes once the window has drawn its new
    /// frame. The cover is drawn off-screen first and only then moved into place, since a move
    /// needs no new drawing and is on screen at once.
    /// </remarks>
    private void Freeze(int newLeft, double newClientWidth, Action apply, Action then)
    {
        PixelRect? frame = WindowBounds.GetFrame(_window);
        if (frame is not { } f)
        {
            apply();
            then();
            return;
        }

        // Before, after, and the stray frame in between: the old size at the new position.
        int left = f.X + (newLeft - _window.Position.X);
        int width = f.Width + (int)Math.Round((newClientWidth - _window.ClientSize.Width) * _window.RenderScaling);
        int areaLeft = Math.Min(f.X, left);
        int areaRight = Math.Max(f.Right, Math.Max(left + f.Width, left + width));
        PixelRect area = new(areaLeft, f.Y, areaRight - areaLeft, f.Height);

        WriteableBitmap picture = WindowBounds.CaptureScreen(area);
        if (picture == null)
        {
            apply();
            then();
            return;
        }

        Window cover = Cover();
        Image image = new()
        {
            Source = picture,
            Stretch = Stretch.Fill,
            Width = area.Width / _window.DesktopScaling,
            Height = area.Height / _window.DesktopScaling,
        };
        _coverCanvas.Children.Clear();
        _coverCanvas.Children.Add(image);
        _coverCanvas.Opacity = 0;
        cover.Width = image.Width;
        cover.Height = image.Height;
        cover.Position = area.Position;

        // The cover is see-through until it has drawn the picture in place; a window only draws
        // reliably on screen, and one that has not yet drawn its new size shows its old one.
        // Then the picture shows, the window changes underneath, and the picture goes once the
        // window has drawn its new frame.
        AfterFrames(cover, 3, () =>
        {
            _coverCanvas.Opacity = 1;
            AfterFrames(cover, 2, () =>
            {
                apply();
                AfterFrames(_window, 3, () =>
                {
                    _coverCanvas.Opacity = 0;
                    AfterFrames(cover, 2, () =>
                    {
                        cover.Position = OffScreen;
                        _coverCanvas.Children.Clear();
                        picture.Dispose();
                    });
                    then();
                });
            });
        });
    }

    private static void AfterFrames(TopLevel level, int frames, Action action)
    {
        if (frames <= 0)
        {
            action();
            return;
        }

        level.RequestAnimationFrame(_ => AfterFrames(level, frames - 1, action));
    }

    private static readonly PixelPoint OffScreen = new(-32000, -32000);

    /// <summary>
    /// The window that holds the still picture: see-through, opened with the main window and kept
    /// off-screen between uses, so the stray frames a window draws when first shown happen long
    /// before it is needed and out of sight.
    /// </summary>
    private Window Cover()
    {
        if (_cover != null) return _cover;

        _cover = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            SizeToContent = SizeToContent.Manual,
            Width = 1,
            Height = 1,
            Position = OffScreen,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            Content = _coverCanvas,
        };
        _cover.Show(_window);
        _cover.Position = OffScreen;
        _window.Closed += (_, _) => _cover?.Close();
        return _cover;
    }

    /// <summary>The window's left edge, in pixels, for what the panels currently add to it.</summary>
    private int TargetX()
        => _baseX - (int)Math.Round((_left.Shifted + _right.Shifted) * _window.DesktopScaling);

    /// <summary>Sizes and places the window for what the panels currently add to it.</summary>
    private void ApplyWindow()
    {
        if (!_pinned) return;

        // Set from the pinned base every frame, never added to the current values: the window
        // writes the size the platform reports back into Width, which trails what was asked for
        // while it resizes, and adding to that drifted by a few pixels per animation.
        double width = _baseWidth + _left.Grown + _right.Grown;

        // The window's position is in pixels and its size in logical units; DesktopScaling is the
        // factor between them, not RenderScaling. The two agree on Windows and X11, but macOS places
        // windows in points: its DesktopScaling is 1 while a Retina display renders at 2.
        int x = TargetX();

        // A moving left edge needs the move and the resize in one step, or the window shows a
        // frame at its new position with its old width.
        bool moved = x != _window.Position.X;
        if (!moved || !WindowBounds.TrySetLeftAndWidth(_window, x, width))
        {
            if (Math.Abs(width - _window.Width) > 0.01)
                _window.Width = width;
            if (moved)
                _window.Position = new PixelPoint(x, _window.Position.Y);
        }

        if (_restoreSizeToContent && _contentHeight != null)
        {
            double height = _contentHeight();
            if (Math.Abs(height - _window.Height) > 0.5)
                _window.Height = height;
        }
    }

    private void Finish(Side side)
    {
        if (side.IsOpen)
        {
            Settle();
            return;
        }

        // The window gives the panel's room back in one step, which moves its left edge.
        if (side.Jumps && _pinned)
        {
            if (TryShrinkEdge(side)) return;

            Side other = ReferenceEquals(side, _left) ? _right : _left;
            int left = _baseX - (int)Math.Round(other.Shifted * _window.DesktopScaling);
            Freeze(left, _baseWidth + other.Grown, () => Close(side), () => { });
            return;
        }

        Close(side);
    }

    /// <summary>
    /// Draws the window's left edge moving back in by the panel's room, where all of that room is on
    /// the left; the panel travels with the edge as it shows when this starts.
    /// </summary>
    private bool TryShrinkEdge(Side side)
    {
        int grow = (int)Math.Round(side.Shifted * _window.DesktopScaling);
        bool leftOnly = Math.Abs(side.Grown - side.Shifted) < 0.5;
        return leftOnly && OperatingSystem.IsWindows() &&
            WindowEdgeGrowth.TryRun(_window, Cover(), _coverCanvas, grow, opening: false, Duration, Easing,
                () => Close(side), null, () => { });
    }

    private void Close(Side side)
    {
        side.Host.IsVisible = false;
        side.Host.Width = 0;
        side.Shown = 0;
        side.Grown = 0;
        side.Shifted = 0;
        side.ShiftTo = 0;
        ApplyWindow();
        Settle();
    }

    /// <summary>Lets the window size itself to its content again once nothing moves.</summary>
    private void Settle()
    {
        if (_left.Running || _right.Running) return;

        Unpin();
    }

    /// <summary>Holds the window at its current size, so the frames can set it by hand.</summary>
    private void Pin()
    {
        if (_pinned || _window.WindowState != WindowState.Normal) return;

        _pinned = true;
        _restoreSizeToContent = _window.SizeToContent == SizeToContent.WidthAndHeight;

        // The window without what the panels add to it, so every frame can be placed from scratch.
        Size client = _window.ClientSize;
        double width = _window.SizeToContent == SizeToContent.Manual && !double.IsNaN(_window.Width)
            ? _window.Width
            : client.Width;
        _baseWidth = width - _left.Grown - _right.Grown;
        _baseX = _window.Position.X + (int)Math.Round((_left.Shifted + _right.Shifted) * _window.DesktopScaling);

        _window.SizeToContent = SizeToContent.Manual;
        _window.Width = width;
        if (_restoreSizeToContent || double.IsNaN(_window.Height)) _window.Height = client.Height;
    }

    private void Unpin()
    {
        if (!_pinned) return;

        _pinned = false;
        if (!_restoreSizeToContent || _window.WindowState != WindowState.Normal) return;

        _window.ClearValue(Window.WidthProperty);
        _window.ClearValue(Window.HeightProperty);
        _window.SizeToContent = SizeToContent.WidthAndHeight;
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
