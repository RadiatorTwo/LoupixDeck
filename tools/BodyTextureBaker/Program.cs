// BodyTextureBaker — regenerates the Loupedeck Live S body SVG
// (Assets/loupedeck-gehaeuse.svg) from a matte source texture.
//
// Why this exists: Skia renders SVG radial gradients in 8-bit WITHOUT dithering,
// so a smooth sheen/vignette gradient bands visibly. Instead of shipping SVG
// gradients, this tool bakes the whole surface — dark texture + lighting — into a
// single 8-bit grayscale image using Floyd-Steinberg error diffusion. The dithered
// image is the band-free representation and stays band-free at every display scale
// (including HiDPI), because up-scaling averages the dither back toward the true value.
//
// Pipeline: load texture -> resize -> damp grain contrast -> compute base tone,
// vignette and sheen in floating point -> Floyd-Steinberg quantize to 8-bit ->
// embed as a Gray8 PNG data-URI in the SVG (clipped to the rounded body, with an
// outer drop shadow). No SVG gradients remain, so nothing can band.
//
// Usage (from anywhere in the repo):
//   dotnet run --project tools/BodyTextureBaker
//   dotnet run --project tools/BodyTextureBaker -- --grain 0.35 --dark 0.55
//
// Options (defaults reproduce the committed SVG):
//   --input  <path>   source texture        (default: texture-no-light.png next to the tool)
//   --output <path>   SVG to write          (default: <repo>/Assets/loupedeck-gehaeuse.svg)
//   --width  <int>    baked image width px  (default: 1100; height derived from body 750x420)
//   --dark   <0..1>   brightness multiplier (default: 0.6)
//   --grain  <0..1>   grain contrast        (default: 0.5; 1 = full texture grain, 0 = smooth)

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

var opts = ParseArgs(args);
if (opts is null) return 0; // --help printed

string repoRoot = FindRepoRoot();
string toolDir = Path.Combine(repoRoot, "tools", "BodyTextureBaker");
string input = opts.Input ?? Path.Combine(toolDir, "texture-no-light.png");
string output = opts.Output ?? Path.Combine(repoRoot, "Assets", "loupedeck-gehaeuse.svg");

if (!File.Exists(input))
{
    Console.Error.WriteLine($"Source texture not found: {input}");
    return 1;
}

// --- Body geometry in SVG viewBox units (matches the AXAML overlay). Sized to the
//     real device: at 5 units/mm the touch bezel is 500x290 (100x58mm) at (200,125),
//     and the body leaves 25/25/10/16mm (125/125/50/80 units) margins around it ->
//     body (75,75)-(825,495) = 750x420 within the unchanged 900x540 viewBox. ---
const int BodyX = 75, BodyY = 75, BodyW = 750, BodyH = 420, BodyR = 60;
int W = opts.Width;
int H = (int)Math.Round(W * (double)BodyH / BodyW);

// --- Lighting: same radial gradients the SVG used, evaluated in object-bounding-box
//     space so the result matches the former <radialGradient> rendering. ---
(double off, double op)[] sheen =
{
    (0.00, 0.13), (0.15, 0.10), (0.30, 0.072), (0.45, 0.048),
    (0.60, 0.028), (0.78, 0.012), (1.00, 0.0),
};
(double off, double op)[] vignette =
{
    (0.45, 0.0), (0.60, 0.06), (0.72, 0.12), (0.84, 0.20), (0.93, 0.27), (1.00, 0.32),
};
const double SheenCx = 0.42, SheenCy = 0.30, SheenR = 0.85;
const double VigCx = 0.5, VigCy = 0.5, VigR = 0.72;

using var orig = SKBitmap.Decode(input);
if (orig is null)
{
    Console.Error.WriteLine($"Could not decode texture: {input}");
    return 1;
}

var info = new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul);
using var resized = orig.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

// Small-radius low-pass to isolate the fine grain (texture minus low-pass = grain).
using var lowSurf = SKSurface.Create(info);
using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.2f, 2.2f) })
    lowSurf.Canvas.DrawBitmap(resized!, 0, 0, paint);
using var low = SKBitmap.FromImage(lowSurf.Snapshot());

static double Lum(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

// --- Continuous luminance field (double precision, no quantization yet). ---
var field = new double[W * H];
for (int y = 0; y < H; y++)
for (int x = 0; x < W; x++)
{
    double lo = Lum(low.GetPixel(x, y));
    double hi = Lum(resized!.GetPixel(x, y));
    double l = (lo + (hi - lo) * opts.Grain) * opts.Dark;   // grain contrast scaled

    double nx = (x + 0.5) / W, ny = (y + 0.5) / H;          // object-bounding-box coords
    double tv = Math.Sqrt((nx - VigCx) * (nx - VigCx) + (ny - VigCy) * (ny - VigCy)) / VigR;
    l *= 1 - Interp(vignette, tv);                          // vignette: black over

    double ts = Math.Sqrt((nx - SheenCx) * (nx - SheenCx) + (ny - SheenCy) * (ny - SheenCy)) / SheenR;
    double a = Interp(sheen, ts);
    l = l * (1 - a) + 255 * a;                              // sheen: white over

    field[y * W + x] = l;
}

// --- Floyd-Steinberg error diffusion to 8-bit. ---
var outBytes = new byte[W * H];
for (int y = 0; y < H; y++)
for (int x = 0; x < W; x++)
{
    double oldVal = field[y * W + x];
    int newVal = (int)Math.Clamp(Math.Round(oldVal), 0, 255);
    double err = oldVal - newVal;
    outBytes[y * W + x] = (byte)newVal;
    Spread(field, W, H, x + 1, y, err * 7.0 / 16);
    Spread(field, W, H, x - 1, y + 1, err * 3.0 / 16);
    Spread(field, W, H, x, y + 1, err * 5.0 / 16);
    Spread(field, W, H, x + 1, y + 1, err * 1.0 / 16);
}

var grayInfo = new SKImageInfo(W, H, SKColorType.Gray8, SKAlphaType.Opaque);
using var gray = new SKBitmap(grayInfo);
Marshal.Copy(outBytes, 0, gray.GetPixels(), outBytes.Length);
using var grayImg = SKImage.FromBitmap(gray);
using var grayData = grayImg.Encode(SKEncodedImageFormat.Png, 100);
string b64 = Convert.ToBase64String(grayData.ToArray());

// --- Assemble the SVG: outer drop shadow + baked image clipped to the rounded body. ---
string head =
    "<svg viewBox=\"0 0 900 540\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" +
    "<defs>" +
    "<filter id=\"bodyShadow\" x=\"-25%\" y=\"-25%\" width=\"150%\" height=\"160%\">" +
    "<feDropShadow dx=\"0\" dy=\"8\" stdDeviation=\"14\" flood-color=\"#000000\" flood-opacity=\"0.45\"/></filter>" +
    $"<clipPath id=\"bodyClip\"><rect x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" rx=\"{BodyR}\" ry=\"{BodyR}\"/></clipPath>" +
    "</defs>" +
    $"<g filter=\"url(#bodyShadow)\"><rect x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" rx=\"{BodyR}\" ry=\"{BodyR}\" fill=\"#151414\"/></g>" +
    "<g clip-path=\"url(#bodyClip)\">" +
    $"<image x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" preserveAspectRatio=\"none\" xlink:href=\"data:image/png;base64,";
string tail = "\"/></g></svg>";
File.WriteAllText(output, head + b64 + tail, new UTF8Encoding(false));

Console.WriteLine($"Baked {W}x{H} (dark={opts.Dark}, grain={opts.Grain}) -> {output}");
Console.WriteLine($"PNG {grayData.Size / 1024.0:F0} KB, SVG {(head.Length + b64.Length + tail.Length) / 1024.0:F0} KB");
return 0;

// ---------- helpers ----------

static double Interp((double off, double op)[] stops, double t)
{
    if (t <= stops[0].off) return stops[0].op;
    if (t >= stops[^1].off) return stops[^1].op;
    for (int i = 1; i < stops.Length; i++)
        if (t <= stops[i].off)
        {
            var (po, pa) = stops[i - 1];
            var (qo, qa) = stops[i];
            return pa + (qa - pa) * ((t - po) / (qo - po));
        }
    return stops[^1].op;
}

static void Spread(double[] buf, int w, int h, int x, int y, double v)
{
    if (x >= 0 && x < w && y >= 0 && y < h) buf[y * w + x] += v;
}

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LoupixDeck.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    // Fallback: tool lives at <root>/tools/BodyTextureBaker, bin output a few levels down.
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

static Options? ParseArgs(string[] args)
{
    var o = new Options();
    for (int i = 0; i < args.Length; i++)
    {
        string a = args[i];
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {a}");
        switch (a)
        {
            case "--input": o.Input = Next(); break;
            case "--output": o.Output = Next(); break;
            case "--width": o.Width = int.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "--dark": o.Dark = double.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "--grain": o.Grain = double.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "-h" or "--help":
                Console.WriteLine("Regenerates Assets/loupedeck-gehaeuse.svg from a matte texture.");
                Console.WriteLine("Options: --input <path> --output <path> --width <int> --dark <0..1> --grain <0..1>");
                Console.WriteLine("Defaults reproduce the committed SVG (width 1100, dark 0.6, grain 0.5).");
                return null;
            default:
                throw new ArgumentException($"Unknown argument: {a}");
        }
    }
    return o;
}

sealed class Options
{
    public string? Input;
    public string? Output;
    public int Width = 1100;
    public double Dark = 0.6;
    public double Grain = 0.5;
}
