using LoupixDeck.Commands.Base;
using SkiaSharp;

namespace LoupixDeck.Commands.DynamicText;

[Command("DynamicText.AnalogClock", "Analog Clock", "Dynamic Text",
    parameterTemplate: "({Seconds})",
    parameterNames: ["Seconds"],
    parameterTypes: [typeof(string)])]
public sealed class AnalogClockCommand : IDynamicImageProvider
{
    private static readonly SKColor FaceColor = new(0x1C, 0x20, 0x26);
    private static readonly SKColor RimColor = new(0x4A, 0x52, 0x5E);
    private static readonly SKColor MajorTickColor = SKColors.White;
    private static readonly SKColor MinorTickColor = new(0x8A, 0x93, 0xA0);
    private static readonly SKColor HandColor = SKColors.White;
    private static readonly SKColor SecondHandColor = new(0xFF, 0x4B, 0x3A);

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    public Task Execute(string[] parameters) => Task.CompletedTask;

    public bool Render(string[] parameters, SKCanvas canvas, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bool showSeconds = !(parameters.Length > 0
                             && bool.TryParse(parameters[0].Trim(), out bool parsed)
                             && !parsed);

        DrawClock(canvas, width, height, DateTime.Now, showSeconds);
        return true;
    }

    private static void DrawClock(SKCanvas canvas, int width, int height, DateTime time, bool showSeconds)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        float radius = (Math.Min(width, height) / 2f) * 0.94f;

        using SKPaint paint = new() { IsAntialias = true };

        // Face and rim
        paint.Style = SKPaintStyle.Fill;
        paint.Color = FaceColor;
        canvas.DrawCircle(cx, cy, radius, paint);

        float rimWidth = Math.Max(1f, radius * 0.045f);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = rimWidth;
        paint.Color = RimColor;
        canvas.DrawCircle(cx, cy, radius - (rimWidth / 2f), paint);

        // Hour ticks; the quarter hours are longer and brighter
        paint.StrokeCap = SKStrokeCap.Round;
        float tickOuter = radius - (rimWidth * 2.2f);
        for (int i = 0; i < 12; i++)
        {
            bool major = (i % 3) == 0;
            float length = radius * (major ? 0.17f : 0.09f);
            paint.StrokeWidth = Math.Max(1f, radius * (major ? 0.06f : 0.035f));
            paint.Color = major ? MajorTickColor : MinorTickColor;
            DrawRadial(canvas, paint, cx, cy, i * 30f, tickOuter - length, tickOuter);
        }

        // Hands advance continuously so the hour hand sits between marks
        float seconds = time.Second;
        float minutes = time.Minute + (seconds / 60f);
        float hours = (time.Hour % 12) + (minutes / 60f);

        paint.Color = HandColor;
        paint.StrokeWidth = Math.Max(1.5f, radius * 0.1f);
        DrawRadial(canvas, paint, cx, cy, hours * 30f, -radius * 0.08f, radius * 0.5f);

        paint.StrokeWidth = Math.Max(1.2f, radius * 0.065f);
        DrawRadial(canvas, paint, cx, cy, minutes * 6f, -radius * 0.08f, radius * 0.76f);

        if (showSeconds)
        {
            paint.Color = SecondHandColor;
            paint.StrokeWidth = Math.Max(1f, radius * 0.03f);
            DrawRadial(canvas, paint, cx, cy, seconds * 6f, -radius * 0.2f, radius * 0.84f);
        }

        // Center cap
        paint.Style = SKPaintStyle.Fill;
        paint.Color = showSeconds ? SecondHandColor : HandColor;
        canvas.DrawCircle(cx, cy, Math.Max(1.5f, radius * 0.075f), paint);
        paint.Color = FaceColor;
        canvas.DrawCircle(cx, cy, Math.Max(0.6f, radius * 0.03f), paint);
    }

    /// <summary>
    /// Draws a line along the ray at <paramref name="degrees"/> clockwise from 12 o'clock,
    /// from distance <paramref name="from"/> to <paramref name="to"/>. A negative
    /// <paramref name="from"/> extends the line behind the center as a counterweight.
    /// </summary>
    private static void DrawRadial(SKCanvas canvas, SKPaint paint, float cx, float cy,
        float degrees, float from, float to)
    {
        double angle = degrees * Math.PI / 180.0;
        float dx = (float)Math.Sin(angle);
        float dy = (float)-Math.Cos(angle);
        canvas.DrawLine(cx + (dx * from), cy + (dy * from), cx + (dx * to), cy + (dy * to), paint);
    }
}
