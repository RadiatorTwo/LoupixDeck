// BodyTextureBaker — regenerates the Loupedeck Live S body SVGs
// (Assets/loupedeck-gehaeuse.svg for Dark mode, Assets/loupedeck-gehaeuse-light.svg
// for Light mode) from matte source textures.
//
// Why this exists: Skia renders SVG radial gradients in 8-bit WITHOUT dithering,
// so a smooth sheen/vignette gradient bands visibly. Instead of shipping SVG
// gradients, this tool bakes the whole surface — texture + lighting — into a
// single 8-bit grayscale image using Floyd-Steinberg error diffusion. The dithered
// image is the band-free representation and stays band-free at every display scale
// (including HiDPI), because up-scaling averages the dither back toward the true value.
//
// Pipeline: load texture -> resize -> damp grain contrast -> compute base tone,
// vignette and sheen in floating point -> Floyd-Steinberg quantize to 8-bit ->
// embed as a Gray8 PNG data-URI in the SVG (clipped to the rounded body, with an
// outer drop shadow). No SVG gradients remain, so nothing can band.
//
// Two variants are baked from two source textures:
//   dark  — texture-no-light.png (near-black) -> loupedeck-gehaeuse.svg
//   light — texture-light.png    (near-white) -> loupedeck-gehaeuse-light.svg
// The Light variant keeps the texture bright (high --dark multiplier) and softens
// the vignette/sheen so the white body does not pick up dirty edges or blown
// highlights. The AXAML device layout picks the matching SVG per ThemeVariant.
//
// Usage (from anywhere in the repo):
//   dotnet run --project tools/BodyTextureBaker                 # bake both variants
//   dotnet run --project tools/BodyTextureBaker -- --variant light
//   dotnet run --project tools/BodyTextureBaker -- --variant dark --grain 0.35 --dark 0.55
//
// Options (defaults reproduce the committed SVGs):
//   --variant <dark|light|both>  which variant(s) to bake  (default: both)
//   --input  <path>   source texture        (default: per-variant texture next to the tool)
//   --output <path>   SVG to write          (default: per-variant SVG under <repo>/Assets)
//   --width  <int>    baked image width px  (default: 1100; height derived from body 750x420)
//   --dark   <0..1>   brightness multiplier (default: 0.6 dark / 0.92 light)
//   --grain  <0..1>   grain contrast        (default: 0.5; 1 = full texture grain, 0 = smooth)
//   --input/--output require a single --variant (they cannot target both at once).

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

var opts = ParseArgs(args);
if (opts is null) return 0; // --help printed

string repoRoot = FindRepoRoot();
string toolDir = Path.Combine(repoRoot, "tools", "BodyTextureBaker");
string assetsDir = Path.Combine(repoRoot, "Assets");

// --- Per-variant defaults. The lighting stops below are shared; each variant
//     scales the vignette/sheen, sets its own base brightness + shadow fill, and
//     carries the body geometry (viewBox + rounded-rect) it bakes into.
//
//     The matte textures are generic surfaces (grain + lighting only), so the
//     same two textures drive every device; only the geometry differs per model:
//       Live S — viewBox 900x540, body (75,75)-(825,495) = 750x420, r60
//                (touch bezel 500x290 at 5 units/mm; see LoupedeckLiveSLayout.axaml).
//       Razer Stream Controller — viewBox 900x600, body (80,50)-(820,555) = 740x505,
//                r42 (aspect 1.465, matched to the device top-view photo). The body
//                is symmetric about x=450; the 480x270 display is centred (x=210-690,
//                y130-400), a knob column sits on each side (centres x130/x770,
//                y154/265/376), and the row of 8 round LED buttons + RAZER wordmark
//                live in the taller bottom margin (button centres y473). ---
var liveGeom = (VW: 900, VH: 540, BX: 75, BY: 75, BW: 750, BH: 420, BR: 60);
var razerGeom = (VW: 900, VH: 600, BX: 80, BY: 50, BW: 740, BH: 505, BR: 42);

string texDark = Path.Combine(toolDir, "texture-no-light.png");
string texLight = Path.Combine(toolDir, "texture-light.png");

var allVariants = new Dictionary<string, Variant>
{
    ["dark"] = new(
        Name: "dark", Input: texDark,
        Output: Path.Combine(assetsDir, "loupedeck-gehaeuse.svg"),
        Dark: 0.6, Grain: 0.5,
        BodyFill: "#151414", VignetteScale: 1.0, SheenScale: 1.0, Geom: liveGeom),
    // Keep the white texture bright; soften edge darkening and the white sheen so
    // a light body does not read as dirty or blown out.
    ["light"] = new(
        Name: "light", Input: texLight,
        Output: Path.Combine(assetsDir, "loupedeck-gehaeuse-light.svg"),
        Dark: 0.92, Grain: 0.5,
        BodyFill: "#d9d9d9", VignetteScale: 0.55, SheenScale: 0.5, Geom: liveGeom),
    ["razer-dark"] = new(
        Name: "razer-dark", Input: texDark,
        Output: Path.Combine(assetsDir, "razer-gehaeuse.svg"),
        Dark: 0.6, Grain: 0.5,
        BodyFill: "#151414", VignetteScale: 1.0, SheenScale: 1.0, Geom: razerGeom),
    ["razer-light"] = new(
        Name: "razer-light", Input: texLight,
        Output: Path.Combine(assetsDir, "razer-gehaeuse-light.svg"),
        Dark: 0.92, Grain: 0.5,
        BodyFill: "#d9d9d9", VignetteScale: 0.55, SheenScale: 0.5, Geom: razerGeom),
};

// Group selectors expand to one or more concrete variants; single names map 1:1.
string[] selected = opts.Variant switch
{
    "both" => ["dark", "light"],
    "razer" => ["razer-dark", "razer-light"],
    "all" => ["dark", "light", "razer-dark", "razer-light"],
    _ => [opts.Variant],
};
var variants = selected.Select(n => allVariants[n]).ToList();

// CLI --input/--output target a single file, so they cannot be combined with "both".
if ((opts.Input is not null || opts.Output is not null) && variants.Count != 1)
{
    Console.Error.WriteLine("--input/--output require a single --variant (dark or light), not 'both'.");
    return 1;
}

int rc = 0;
foreach (var v in variants)
{
    string input = opts.Input ?? v.Input;
    string output = opts.Output ?? v.Output;
    double dark = opts.Dark ?? v.Dark;
    double grain = opts.Grain ?? v.Grain;
    rc = Bake(v, input, output, opts.Width, dark, grain);
    if (rc != 0) return rc;
}
return rc;

static int Bake(Variant variant, string input, string output, int W, double dark, double grain)
{
    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Source texture not found: {input}");
        return 1;
    }

    // --- Body geometry in SVG viewBox units (matches the AXAML overlay), supplied
    //     per variant so the same texture/lighting pipeline bakes any device body. ---
    var (ViewBoxW, ViewBoxH, BodyX, BodyY, BodyW, BodyH, BodyR) = variant.Geom;
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

    static double Lum(SKColor c) => (0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue);

    // --- Continuous luminance field (double precision, no quantization yet). ---
    var field = new double[W * H];
    for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        double lo = Lum(low.GetPixel(x, y));
        double hi = Lum(resized!.GetPixel(x, y));
        double l = (lo + ((hi - lo) * grain)) * dark;             // grain contrast scaled

        double nx = (x + 0.5) / W, ny = (y + 0.5) / H;          // object-bounding-box coords
        double tv = Math.Sqrt(((nx - VigCx) * (nx - VigCx)) + ((ny - VigCy) * (ny - VigCy))) / VigR;
        l *= 1 - (Interp(vignette, tv) * variant.VignetteScale);  // vignette: black over

        double ts = Math.Sqrt(((nx - SheenCx) * (nx - SheenCx)) + ((ny - SheenCy) * (ny - SheenCy))) / SheenR;
        double a = Interp(sheen, ts) * variant.SheenScale;
        l = (l * (1 - a)) + (255 * a);                              // sheen: white over

        field[(y * W) + x] = l;
    }

    // --- Floyd-Steinberg error diffusion to 8-bit. ---
    var outBytes = new byte[W * H];
    for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        double oldVal = field[(y * W) + x];
        int newVal = (int)Math.Clamp(Math.Round(oldVal), 0, 255);
        double err = oldVal - newVal;
        outBytes[(y * W) + x] = (byte)newVal;
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
        $"<svg viewBox=\"0 0 {ViewBoxW} {ViewBoxH}\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" +
        "<defs>" +
        "<filter id=\"bodyShadow\" x=\"-25%\" y=\"-25%\" width=\"150%\" height=\"160%\">" +
        "<feDropShadow dx=\"0\" dy=\"8\" stdDeviation=\"14\" flood-color=\"#000000\" flood-opacity=\"0.45\"/></filter>" +
        $"<clipPath id=\"bodyClip\"><rect x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" rx=\"{BodyR}\" ry=\"{BodyR}\"/></clipPath>" +
        "</defs>" +
        $"<g filter=\"url(#bodyShadow)\"><rect x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" rx=\"{BodyR}\" ry=\"{BodyR}\" fill=\"{variant.BodyFill}\"/></g>" +
        "<g clip-path=\"url(#bodyClip)\">" +
        $"<image x=\"{BodyX}\" y=\"{BodyY}\" width=\"{BodyW}\" height=\"{BodyH}\" preserveAspectRatio=\"none\" xlink:href=\"data:image/png;base64,";
    string tail = "\"/></g></svg>";
    File.WriteAllText(output, head + b64 + tail, new UTF8Encoding(false));

    Console.WriteLine($"Baked {variant.Name} {W}x{H} (dark={dark}, grain={grain}) -> {output}");
    Console.WriteLine($"PNG {grayData.Size / 1024.0:F0} KB, SVG {(head.Length + b64.Length + tail.Length) / 1024.0:F0} KB");
    return 0;
}

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
            return pa + ((qa - pa) * ((t - po) / (qo - po)));
        }
    return stops[^1].op;
}

static void Spread(double[] buf, int w, int h, int x, int y, double v)
{
    if (x >= 0 && x < w && y >= 0 && y < h) buf[(y * w) + x] += v;
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
            case "--variant":
                o.Variant = Next().ToLowerInvariant();
                if (o.Variant is not ("dark" or "light" or "both"
                    or "razer-dark" or "razer-light" or "razer" or "all"))
                    throw new ArgumentException(
                        "--variant must be dark, light, both, razer-dark, razer-light, razer or all");
                break;
            case "--input": o.Input = Next(); break;
            case "--output": o.Output = Next(); break;
            case "--width": o.Width = int.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "--dark": o.Dark = double.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "--grain": o.Grain = double.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "-h" or "--help":
                Console.WriteLine("Regenerates the device body SVGs (Live S + Razer, Dark + Light) from matte textures.");
                Console.WriteLine("Options: --variant <dark|light|both|razer-dark|razer-light|razer|all> --input <path> --output <path> --width <int> --dark <0..1> --grain <0..1>");
                Console.WriteLine("Defaults reproduce the committed SVGs (width 1100; dark 0.6/grain 0.5 for dark, dark 0.92/grain 0.5 for light).");
                return null;
            default:
                throw new ArgumentException($"Unknown argument: {a}");
        }
    }
    return o;
}

sealed class Options
{
    public string Variant = "both";
    public string? Input;
    public string? Output;
    public int Width = 1100;
    public double? Dark;
    public double? Grain;
}

sealed record Variant(
    string Name,
    string Input,
    string Output,
    double Dark,
    double Grain,
    string BodyFill,
    double VignetteScale,
    double SheenScale,
    (int VW, int VH, int BX, int BY, int BW, int BH, int BR) Geom);
