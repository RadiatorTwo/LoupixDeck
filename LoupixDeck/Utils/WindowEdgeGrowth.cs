using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LoupixDeck.Utils;

/// <summary>
/// Shows a window's left edge moving out or in, animated, without moving the window per frame.
/// </summary>
/// <remarks>
/// A window whose left edge moves shows every new position for a frame with its previous content,
/// so moving the real edge frame by frame makes its content jitter, whatever Avalonia's rendering
/// mode. The real window changes size once, hidden under a see-through cover window, and the
/// movement is drawn in the cover instead.
///
/// Opening, the window is already wide while the edge moves out: the cover only hides what lies
/// left of the moving edge behind a picture of the desktop taken beforehand, and draws the
/// window's left end (title bar with icon and title, frame) at the edge. Everything right of it is
/// the real window, so whatever it shows there — the panel sliding out — moves with the edge.
///
/// Closing, the window is already narrow while the edge moves in: the cover shows a picture of the
/// wide window, the deck still and the panel travelling with the edge, in front of an extension in
/// the window's own colours that shrinks.
///
/// Colours and the title bar's left end are taken from a picture of the screen, so what the cover
/// draws looks like the window it stands in for, in either theme and whether or not the window is
/// active.
/// </remarks>
public static class WindowEdgeGrowth
{
    /// <summary>
    /// Animates the left edge of <paramref name="window"/> moving out by <paramref name="growPixels"/>
    /// (<paramref name="opening"/>) or back in by as much. <paramref name="resize"/> changes the real
    /// window, hidden, before the edge moves. <paramref name="progress"/> gets, on every frame of an
    /// opening, how far the edge has moved out, from 0 to 1, so the window's content can move with
    /// it. <paramref name="done"/> runs once the real window shows on its own again. Returns false,
    /// having done nothing, where the platform cannot provide what this needs; the caller then
    /// resizes plainly.
    /// </summary>
    public static bool TryRun(Window window, Window cover, Canvas canvas, int growPixels, bool opening,
        TimeSpan duration, Easing easing, Action resize, Action<double> progress, Action done)
    {
        if (growPixels <= 0 ||
            WindowBounds.GetFrame(window) is not { } frame ||
            WindowBounds.GetVisibleFrame(window) is not { } visible ||
            window.PointToScreen(new Point(0, 0)) is var client && client.Y <= visible.Y)
            return false;

        double scale = window.DesktopScaling;

        // The cover spans from the wide window's left edge to the frame's right end.
        int coverLeft = opening ? visible.X - growPixels : visible.X;
        PixelRect area = new(coverLeft, frame.Y, frame.Right - coverLeft, frame.Height);

        WriteableBitmap shot = WindowBounds.CaptureScreen(frame);
        if (shot == null) return false;

        // What lies left of the narrow window, revealed again as the edge moves out.
        WriteableBitmap backdrop = null;
        if (opening)
        {
            backdrop = WindowBounds.CaptureScreen(new PixelRect(coverLeft, frame.Y, growPixels, frame.Height));
            if (backdrop == null)
            {
                shot.Dispose();
                return false;
            }
        }

        // In picture pixels. The narrow window's left edge: where it is now when opening, where it
        // will be when closing.
        int edge = visible.X - frame.X;
        int narrowEdge = opening ? edge : edge + growPixels;
        int top = visible.Y - frame.Y;
        int titleHeight = client.Y - visible.Y;
        int bodyHeight = visible.Bottom - client.Y;
        int clientInset = client.X - visible.X;

        // The title bar's left end (icon and title) travels with the edge; the rest of it is plain.
        int titleWidth = Math.Min((int)Math.Round(220 * scale), (visible.Width - (opening ? 0 : growPixels)) / 3);
        Color titleColour = PixelAt(shot, edge + titleWidth + 2, top + (titleHeight / 2));
        Color bodyColour = PixelAt(shot, edge + clientInset + 2, top + titleHeight + bodyHeight - 4);
        Color frameColour = PixelAt(shot, edge, top + titleHeight + (bodyHeight / 2));
        double radius = Environment.OSVersion.Version.Build >= 22000 ? 8 * scale : 0;
        int cornerSize = (int)Math.Ceiling(radius) + clientInset + 1;

        double Logical(int pixels) => pixels / scale;

        // Still picture of the narrow window, from its left edge on.
        Image still = new()
        {
            Source = new CroppedBitmap(shot, new PixelRect(narrowEdge, 0, shot.PixelSize.Width - narrowEdge, frame.Height)),
            Width = Logical(shot.PixelSize.Width - narrowEdge),
            Height = Logical(frame.Height),
            Stretch = Stretch.Fill,
        };
        Canvas.SetLeft(still, Logical(growPixels));

        Image titleEnd = new()
        {
            Source = new CroppedBitmap(shot, new PixelRect(edge, top, titleWidth, titleHeight)),
            Width = Logical(titleWidth),
            Height = Logical(titleHeight),
            Stretch = Stretch.Fill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };

        Action<double> place;
        canvas.Children.Clear();

        if (opening)
        {
            Border uncovered = new()
            {
                ClipToBounds = true,
                Height = Logical(frame.Height),
                Child = new Image
                {
                    Source = backdrop,
                    Width = Logical(growPixels),
                    Height = Logical(frame.Height),
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                },
            };

            // The narrow window's left frame and rounded lower corner, carried along with the edge.
            Border rim = new()
            {
                Background = new SolidColorBrush(frameColour),
                Width = Logical(1),
                Height = Logical(bodyHeight - cornerSize),
            };
            Canvas.SetTop(rim, Logical(top + titleHeight));
            Image corner = new()
            {
                Source = new CroppedBitmap(shot, new PixelRect(edge, top + titleHeight + bodyHeight - cornerSize, cornerSize, cornerSize)),
                Width = Logical(cornerSize),
                Height = Logical(cornerSize),
                Stretch = Stretch.Fill,
            };
            Canvas.SetTop(corner, Logical(top + titleHeight + bodyHeight - cornerSize));
            Canvas.SetTop(titleEnd, Logical(top));

            place = grown =>
            {
                int left = growPixels - (int)Math.Round(grown);
                uncovered.Width = Logical(left);
                Canvas.SetLeft(rim, Logical(left));
                Canvas.SetLeft(corner, Logical(left));
                Canvas.SetLeft(titleEnd, Logical(left));
            };

            canvas.Children.Add(uncovered);
            canvas.Children.Add(still);
            canvas.Children.Add(rim);
            canvas.Children.Add(corner);
            canvas.Children.Add(titleEnd);
        }
        else
        {
            Border title = new()
            {
                Background = new SolidColorBrush(titleColour),
                CornerRadius = new CornerRadius(radius, 0, 0, 0),
                ClipToBounds = true,
                Height = Logical(titleHeight),
                Child = titleEnd,
            };
            Canvas.SetTop(title, Logical(top));

            // The panel as it shows now, travelling with the edge and cut off where the deck begins.
            Image panel = new()
            {
                Source = new CroppedBitmap(shot, new PixelRect(edge + clientInset, top + titleHeight, growPixels, bodyHeight - 1)),
                Width = Logical(growPixels),
                Height = Logical(bodyHeight - 1),
                Stretch = Stretch.Fill,
            };
            Canvas.SetLeft(panel, Logical(clientInset - 1));
            Canvas panelClip = new()
            {
                ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Children = { panel },
            };

            Border body = new()
            {
                Background = new SolidColorBrush(bodyColour),
                BorderBrush = new SolidColorBrush(frameColour),
                BorderThickness = new Thickness(1, 0, 0, 1),
                CornerRadius = new CornerRadius(0, 0, 0, radius),
                ClipToBounds = true,
                Height = Logical(bodyHeight),
                Child = panelClip,
            };
            Canvas.SetTop(body, Logical(top + titleHeight));

            // The panel's own right-hand rule sits where the narrow window's frame was.
            Border rule = new()
            {
                Background = new SolidColorBrush(frameColour),
                Width = Logical(1),
                Height = Logical(bodyHeight - 1),
            };
            Canvas.SetLeft(rule, Logical(growPixels + clientInset - 1));
            Canvas.SetTop(rule, Logical(top + titleHeight));

            // Covers the narrow window's rounded left corners and frame in the picture.
            int overlap = cornerSize;

            place = grown =>
            {
                int left = growPixels - (int)Math.Round(grown);
                Canvas.SetLeft(title, Logical(left));
                title.Width = Logical((growPixels - left) + titleWidth);
                Canvas.SetLeft(body, Logical(left));
                body.Width = Logical((growPixels - left) + overlap);
                panelClip.Width = Logical(Math.Max(0, (growPixels - left) + clientInset - 2));
                rule.IsVisible = grown >= 1;
            };

            canvas.Children.Add(still);
            canvas.Children.Add(body);
            canvas.Children.Add(rule);
            canvas.Children.Add(title);
        }

        canvas.Opacity = 0;
        place(opening ? 0 : growPixels);

        cover.Width = Logical(area.Width);
        cover.Height = Logical(area.Height);
        cover.Position = area.Position;

        void Finish()
        {
            done();
            canvas.Opacity = 0;
            AfterFrames(cover, 2, () =>
            {
                cover.Position = new PixelPoint(-32000, -32000);
                canvas.Children.Clear();
                shot.Dispose();
                backdrop?.Dispose();
            });
        }

        void Animate(double from, double to, Action then)
        {
            TimeSpan? start = null;
            void Frame(TimeSpan time)
            {
                start ??= time;
                double t = easing.Ease(Math.Clamp((time - start.Value) / duration, 0, 1));
                place(from + ((to - from) * t));
                if (opening) progress?.Invoke(t);
                if (t < 1)
                    cover.RequestAnimationFrame(Frame);
                else
                    then();
            }

            cover.RequestAnimationFrame(Frame);
        }

        // Drawn see-through first, in place; then shown, which looks exactly like the window. The
        // window changes underneath, and once it has drawn its new frame the edge moves.
        AfterFrames(cover, 3, () =>
        {
            canvas.Opacity = 1;
            AfterFrames(cover, 2, () =>
            {
                resize();
                AfterFrames(window, 3, () =>
                {
                    if (opening)
                    {
                        // From here the real window shows right of the edge.
                        canvas.Children.Remove(still);
                        Animate(0, growPixels, Finish);
                    }
                    else
                    {
                        Animate(growPixels, 0, Finish);
                    }
                });
            });
        });

        return true;
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

    private static Color PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        x = Math.Clamp(x, 0, bitmap.PixelSize.Width - 1);
        y = Math.Clamp(y, 0, bitmap.PixelSize.Height - 1);

        using ILockedFramebuffer buffer = bitmap.Lock();
        uint bgra = (uint)Marshal.ReadInt32(buffer.Address + (y * buffer.RowBytes) + (x * 4));
        return Color.FromArgb(255, (byte)(bgra >> 16), (byte)(bgra >> 8), (byte)bgra);
    }
}
