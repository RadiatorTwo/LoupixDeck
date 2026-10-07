using System.Buffers.Binary;
using Avalonia.Media;

namespace LoupixDeck.LoupedeckDevice.Virtual;

/// <summary>
/// What a virtual device's hardware would be showing, rebuilt from the packets the app sends it:
/// one BGRA surface per framebuffer (decoded from the RGB565 FRAMEBUFF writes, published on DRAW
/// exactly like the panel), the LED button colours, and the brightness. Owned by the device so
/// it outlives a reconnect; read by the simulator window.
/// </summary>
/// <param name="displays">The device's framebuffer table. A getter, because the device's base
/// constructor connects before the derived constructor has filled the table in.</param>
public sealed class VirtualDeviceState(Func<IReadOnlyDictionary<string, DisplayInfo>> displays)
{
    private const uint OpaqueBlack = 0xFF000000;

    private sealed class Surface
    {
        public Surface(DisplayInfo info)
        {
            Info = info;
            Back = new uint[info.Width * info.Height];
            Front = new uint[info.Width * info.Height];
            Array.Fill(Back, OpaqueBlack);
            Array.Fill(Front, OpaqueBlack);
        }

        public DisplayInfo Info { get; }
        public uint[] Back { get; }
        public uint[] Front { get; }
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Surface> _surfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<byte, Color> _leds = new();

    /// <summary>A framebuffer was refreshed; the argument is its name ("center", "knob").</summary>
    public event Action<string> FrameChanged;

    /// <summary>An LED button changed colour; the argument is its protocol byte.</summary>
    public event Action<byte> LedChanged;

    public event Action BrightnessChanged;

    public event Action Vibrated;

    /// <summary>Panel brightness, 0..1.</summary>
    public double Brightness { get; private set; } = 1.0;

    public IReadOnlyDictionary<string, DisplayInfo> Displays =>
        displays() ?? new Dictionary<string, DisplayInfo>();

    /// <summary>Decodes a FRAMEBUFF payload ([display id][x, y, w, h][RGB565 pixels]) into the
    /// display's back buffer. Nothing becomes visible before the matching DRAW.</summary>
    public void WriteFramebuffer(ReadOnlySpan<byte> data)
    {
        if (!TryMatchDisplay(data, out string name, out DisplayInfo info))
            return;

        ReadOnlySpan<byte> header = data[info.Id.Length..];
        if (header.Length < 8)
            return;

        int x = BinaryPrimitives.ReadUInt16BigEndian(header);
        int y = BinaryPrimitives.ReadUInt16BigEndian(header[2..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
        int rows = BinaryPrimitives.ReadUInt16BigEndian(header[6..]);
        ReadOnlySpan<byte> pixels = header[8..];

        lock (_gate)
        {
            Surface surface = GetSurface(name, info);
            int surfaceWidth = info.Width;
            int surfaceHeight = info.Height;
            uint[] back = surface.Back;

            for (int row = 0; row < rows; row++)
            {
                int dy = y + row;
                if (dy >= surfaceHeight)
                    break;

                for (int col = 0; col < width; col++)
                {
                    int dx = x + col;
                    int i = ((row * width) + col) * 2;
                    if (i + 1 >= pixels.Length)
                        return;
                    if (dx >= surfaceWidth)
                        continue;

                    ushort rgb565 = info.BigEndianPixels
                        ? (ushort)((pixels[i] << 8) | pixels[i + 1])
                        : (ushort)(pixels[i] | (pixels[i + 1] << 8));
                    back[(dy * surfaceWidth) + dx] = ToBgra(rgb565);
                }
            }
        }
    }

    /// <summary>Publishes a display's back buffer, as the panel does on a DRAW (payload: the display id).</summary>
    public void Present(ReadOnlySpan<byte> data)
    {
        if (!TryMatchDisplay(data, out string name, out DisplayInfo info))
            return;

        lock (_gate)
        {
            Surface surface = GetSurface(name, info);
            Array.Copy(surface.Back, surface.Front, surface.Back.Length);
        }

        FrameChanged?.Invoke(name);
    }

    /// <summary>Copies the visible frame of <paramref name="name"/> into a BGRA8888 buffer.</summary>
    public unsafe void CopyFrame(string name, IntPtr address, int rowBytes)
    {
        lock (_gate)
        {
            // A display nothing was drawn to yet is copied as the black it is on the hardware.
            if (!_surfaces.TryGetValue(name, out Surface surface))
            {
                if (!Displays.TryGetValue(name, out DisplayInfo info))
                    return;
                surface = GetSurface(name, info);
            }

            int width = surface.Info.Width;
            for (int row = 0; row < surface.Info.Height; row++)
            {
                Span<uint> dest = new((byte*)address + ((long)row * rowBytes), width);
                surface.Front.AsSpan(row * width, width).CopyTo(dest);
            }
        }
    }

    public void SetLed(byte key, byte r, byte g, byte b)
    {
        lock (_gate)
            _leds[key] = Color.FromRgb(r, g, b);
        LedChanged?.Invoke(key);
    }

    /// <summary>The colour last set for LED button <paramref name="key"/>, or null when none was set.</summary>
    public Color? GetLed(byte key)
    {
        lock (_gate)
            return _leds.TryGetValue(key, out Color color) ? color : null;
    }

    public void SetBrightness(byte level)
    {
        Brightness = Math.Clamp(level / (double)Constants.MaxBrightness, 0, 1);
        BrightnessChanged?.Invoke();
    }

    public void Vibrate() => Vibrated?.Invoke();

    private Surface GetSurface(string name, DisplayInfo info)
    {
        if (!_surfaces.TryGetValue(name, out Surface surface))
        {
            surface = new Surface(info);
            _surfaces[name] = surface;
        }

        return surface;
    }

    private bool TryMatchDisplay(ReadOnlySpan<byte> data, out string name, out DisplayInfo info)
    {
        foreach ((string key, DisplayInfo display) in Displays)
        {
            if (data.Length >= display.Id.Length && data[..display.Id.Length].SequenceEqual(display.Id))
            {
                name = key;
                info = display;
                return true;
            }
        }

        name = null;
        info = null;
        return false;
    }

    private static uint ToBgra(ushort rgb565)
    {
        uint r = (uint)((((rgb565 >> 11) & 0x1F) * 255) + 15) / 31;
        uint g = (uint)((((rgb565 >> 5) & 0x3F) * 255) + 31) / 63;
        uint b = (uint)(((rgb565 & 0x1F) * 255) + 15) / 31;
        return OpaqueBlack | (r << 16) | (g << 8) | b;
    }
}
