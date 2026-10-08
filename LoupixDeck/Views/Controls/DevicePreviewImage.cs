using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoupixDeck.Models.Converter;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Shows a device image (a key, LED button or dial) in the on-screen device view, sharp at
/// whatever size the view is zoomed to (issue #251). It lays out and starts out exactly like an
/// <see cref="Image"/> of the device bitmap; once that image has stopped changing for a moment,
/// it draws it again at the resolution it is shown at, using the recipe its renderer registered
/// (<see cref="PreviewRecipes"/>).
/// </summary>
/// <remarks>
/// Performance: an image that changes in quick succession (an animation) keeps showing the device
/// frames and is never re-rendered, so animations cost no more than before. Zooming re-renders
/// as soon as the zoom has come to rest. Re-renders run one at a time off the UI thread, and an
/// image shown several times at the same size (the dials' shared knob) is rendered once.
/// </remarks>
public sealed class DevicePreviewImage : Control
{
    /// <summary>The device image: an <see cref="SKBitmap"/> or an Avalonia <see cref="Bitmap"/>.</summary>
    public static readonly StyledProperty<object> DeviceImageProperty =
        AvaloniaProperty.Register<DevicePreviewImage, object>(nameof(DeviceImage));

    // An image whose last change is more recent than this counts as animating.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

    // No sharper than this many times the device image: beyond it the gain is invisible and the
    // memory is not.
    private const double MaxPreviewScale = 5.0;

    // Close enough to 1:1 that the device image itself is as sharp as it gets.
    private const double ScaleTolerance = 0.04;

    private static readonly HashSet<DevicePreviewImage> Pending = [];
    private static DispatcherTimer _scheduler;

    // Previews are drawn one after another on a background thread; each takes the Skia gate.
    private static Task _renderQueue = Task.CompletedTask;

    // The latest preview rendered per device image, weak on the image like its recipe.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, SharedPreview> SharedPreviews = new();

    private Bitmap _deviceBitmap;
    private Bitmap _preview;
    private object _previewSource;
    private double _previewScale;
    private int _generation;
    private long _lastContentChange;
    private long _dueAt;
    private DeviceZoomPanel _zoomPanel;
    private TopLevel _topLevel;

    static DevicePreviewImage()
    {
        AffectsMeasure<DevicePreviewImage>(DeviceImageProperty);
        AffectsRender<DevicePreviewImage>(DeviceImageProperty);
    }

    public object DeviceImage
    {
        get => GetValue(DeviceImageProperty);
        set => SetValue(DeviceImageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != DeviceImageProperty) return;

        _deviceBitmap = change.NewValue switch
        {
            SKBitmap skBitmap => SKBitmapToAvaloniaBitmapConverter.ToBitmap(skBitmap),
            Bitmap bitmap => bitmap,
            _ => null
        };

        // The preview shows the old content now; drop it and let the device image stand in.
        DropPreview();

        // A change after a quiet spell (a page switch, an edit, a clock ticking) is re-rendered
        // right away; one in quick succession waits until the image settles.
        long now = Stopwatch.GetTimestamp();
        bool quiet = Stopwatch.GetElapsedTime(_lastContentChange, now) > SettleTime;
        _lastContentChange = now;
        Schedule(quiet ? TimeSpan.Zero : SettleTime);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _zoomPanel = this.FindAncestorOfType<DeviceZoomPanel>();
        _zoomPanel?.ScaleSettled += OnScaleChanged;
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.ScalingChanged += OnScaleChanged;

        Schedule(SettleTime);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _zoomPanel?.ScaleSettled -= OnScaleChanged;
        _zoomPanel = null;
        _topLevel?.ScalingChanged -= OnScaleChanged;
        _topLevel = null;

        Pending.Remove(this);
        _generation++;
    }

    // The old preview keeps standing in, merely scaled, until the new one is ready. No settle delay:
    // the zoom panel only reports a scale once its animation has come to rest.
    private void OnScaleChanged(object sender, EventArgs e) => Schedule(TimeSpan.Zero);

    private void DropPreview()
    {
        _preview = null;
        _previewSource = null;
        _generation++;
    }

    // ---- Layout and drawing: those of an Image with Stretch="Uniform" -------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        return _deviceBitmap == null ? default : UniformSize(availableSize, _deviceBitmap.Size);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return _deviceBitmap == null ? default : UniformSize(finalSize, _deviceBitmap.Size);
    }

    private static Size UniformSize(Size available, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0) return default;

        double scaleX = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / source.Width;
        double scaleY = double.IsInfinity(available.Height) ? double.PositiveInfinity : available.Height / source.Height;
        double scale = Math.Min(scaleX, scaleY);
        if (double.IsInfinity(scale)) scale = 1;
        return source * scale;
    }

    private Rect DestinationRect()
    {
        Size size = UniformSize(Bounds.Size, _deviceBitmap.Size);
        return new Rect(Bounds.Size).CenterRect(new Rect(size));
    }

    public override void Render(DrawingContext context)
    {
        if (_deviceBitmap == null) return;

        Bitmap image = _preview ?? _deviceBitmap;
        context.DrawImage(image, new Rect(image.Size), DestinationRect());
    }

    // ---- Re-rendering at the on-screen size ----------------------------------------------

    private void Schedule(TimeSpan delay)
    {
        _dueAt = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
        Pending.Add(this);

        if (_scheduler == null)
        {
            _scheduler = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            _scheduler.Tick += (_, _) => RunDue();
        }

        if (delay == TimeSpan.Zero)
            Dispatcher.UIThread.Post(RunDue, DispatcherPriority.Background);

        _scheduler.Start();
    }

    private static void RunDue()
    {
        // Every change pushes an image's due time back, so an animating image never comes due.
        long now = Stopwatch.GetTimestamp();
        foreach (DevicePreviewImage image in Pending.ToArray())
        {
            if (image._dueAt > now) continue;

            Pending.Remove(image);
            image.UpdatePreview();
        }

        if (Pending.Count == 0)
            _scheduler?.Stop();
    }

    private void UpdatePreview()
    {
        object source = DeviceImage;
        Func<double, Bitmap> recipe = PreviewRecipes.Find(source);
        if (recipe == null || _deviceBitmap == null || _topLevel == null || !IsEffectivelyVisible)
            return;

        double scale = OnScreenScale();
        if (double.IsNaN(scale)) return;

        if (Math.Abs(scale - 1) < ScaleTolerance)
        {
            // The device image is already shown pixel for pixel.
            if (_preview != null)
            {
                DropPreview();
                InvalidateVisual();
            }

            return;
        }

        if (_preview != null && ReferenceEquals(_previewSource, source) &&
            Math.Abs(_previewScale - scale) / scale < ScaleTolerance)
            return;

        int generation = ++_generation;
        SharedRender(source, recipe, scale).ContinueWith(task =>
        {
            Bitmap preview = task.Result;
            Dispatcher.UIThread.Post(() =>
            {
                // Something newer has replaced the image or the request in the meantime.
                if (generation != _generation || !ReferenceEquals(DeviceImage, source) || preview == null) return;

                _preview = preview;
                _previewSource = source;
                _previewScale = scale;
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// The preview of <paramref name="source"/> at <paramref name="scale"/>, rendered once however
    /// many controls show the same image at the same size - every dial shows one shared knob image.
    /// </summary>
    private static Task<Bitmap> SharedRender(object source, Func<double, Bitmap> recipe, double scale)
    {
        if (SharedPreviews.TryGetValue(source, out SharedPreview shared) &&
            Math.Abs(shared.Scale - scale) / scale < ScaleTolerance)
            return shared.Render;

        Task<Bitmap> render = _renderQueue.ContinueWith(_ =>
        {
            try
            {
                return recipe(scale);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DevicePreview] Rendering a preview failed: {ex.Message}");
                return null;
            }
        }, TaskScheduler.Default);

        _renderQueue = render;
        SharedPreviews.AddOrUpdate(source, new SharedPreview(scale, render));
        return render;
    }

    private sealed record SharedPreview(double Scale, Task<Bitmap> Render);

    /// <summary>Screen pixels per pixel of the device image, as it is drawn right now.</summary>
    private double OnScreenScale()
    {
        Matrix? toTop = this.TransformToVisual(_topLevel);
        if (toTop is not { } matrix || _deviceBitmap.PixelSize.Width <= 0) return double.NaN;

        double layoutScale = Math.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        double screenWidth = DestinationRect().Width * layoutScale * _topLevel.RenderScaling;
        double scale = screenWidth / _deviceBitmap.PixelSize.Width;
        return scale > 0 ? Math.Min(scale, MaxPreviewScale) : double.NaN;
    }
}
