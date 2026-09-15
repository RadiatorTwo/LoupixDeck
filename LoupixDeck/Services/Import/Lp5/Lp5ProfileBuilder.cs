using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Models.Macros;
using LoupixDeck.Services.AppLauncher;
using SkiaSharp;
using Svg.Skia;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Carries out an <see cref="Lp5ImportPlan"/>: builds the <see cref="Profile"/> and the user macros it
/// refers to, importing key images into the asset store on the way. Attaching the result to the config
/// and the macro store is the caller's job — and must be followed by a config save in the same UI
/// continuation, or the asset cleanup of another device's save could sweep the new images.
/// </summary>
public static class Lp5ProfileBuilder
{
    /// <summary>Pixel size SVG glyphs are rasterized to before they enter the asset store.</summary>
    private const int SvgRasterSize = 128;

    /// <summary>Longest label written onto a key; Loupedeck names are often longer than a key fits.</summary>
    private const int MaxLabelLength = 16;

    public static Profile BuildProfile(Lp5ImportPlan plan, string name, IAssetService assets, int keySizePx)
    {
        Profile profile = new() { Name = name };
        Dictionary<string, string> assetCache = new(StringComparer.Ordinal);

        foreach (Lp5PlannedWorkspace planned in plan.Workspaces)
        {
            Workspace workspace = new() { Name = planned.Name };

            foreach (Lp5PlannedTouchPage plannedPage in planned.TouchPages)
            {
                TouchButtonPage page = new(plannedPage.Keys.Count) { Name = plannedPage.Name };
                for (int i = 0; i < plannedPage.Keys.Count; i++)
                {
                    if (plannedPage.Keys[i] is { } key)
                        ApplyKey(page.TouchButtons[i], key, plan.Package, assets, keySizePx, assetCache);
                }

                workspace.TouchButtonPages.Add(page);
            }

            foreach (Lp5PlannedRotaryPage plannedPage in planned.RotaryPages)
            {
                RotaryButtonPage page = new(plannedPage.Dials.Count) { Name = plannedPage.Name, Side = plannedPage.Side };
                for (int i = 0; i < plannedPage.Dials.Count; i++)
                {
                    if (plannedPage.Dials[i] is { } dial)
                        ApplyDial(page.RotaryButtons[i], dial);
                }

                RotaryPagesOf(workspace, plannedPage.Side).Add(page);
            }

            profile.Workspaces.Add(workspace);
            if (planned.IsHome)
                profile.HomeWorkspaceId = workspace.Id;
        }

        if (profile.Workspaces.Count == 0)
            profile.Workspaces.Add(new Workspace { Name = "Home" });

        if (profile.Workspaces.All(w => w.Id != profile.HomeWorkspaceId))
            profile.HomeWorkspaceId = profile.Workspaces[0].Id;

        return profile;
    }

    /// <summary>Turns the plan's macro drafts into macros for the macro store.</summary>
    public static List<Macro> BuildMacros(Lp5ImportPlan plan)
    {
        List<Macro> macros = [];
        foreach (Lp5MacroDraft draft in plan.Macros)
        {
            Macro macro = new() { Name = draft.Name };
            foreach (Lp5Step step in draft.Steps)
            {
                macro.Steps.Add(step.Kind switch
                {
                    Lp5StepKind.Keys => new KeyCombinationStep { Keys = step.Value },
                    Lp5StepKind.Text => new TextStep { Text = step.Value },
                    _ => new CommandStep { CommandString = step.Value }
                });
            }

            macros.Add(macro);
        }

        return macros;
    }

    private static IList<RotaryButtonPage> RotaryPagesOf(Workspace workspace, RotarySide side) => side switch
    {
        RotarySide.Left => workspace.LeftRotaryButtonPages,
        RotarySide.Right => workspace.RightRotaryButtonPages,
        _ => workspace.RotaryButtonPages
    };

    private static void ApplyDial(RotaryButton button, Lp5PlannedDial dial)
    {
        if (dial.Press?.IsMapped == true)
            button.Command = dial.Press.Command;

        if (dial.Rotate?.IsMapped == true)
        {
            button.RotaryLeftCommand = dial.Rotate.LeftCommand ?? string.Empty;
            button.RotaryRightCommand = dial.Rotate.RightCommand ?? string.Empty;
        }

        string label = dial.Rotate?.Label;
        if (string.IsNullOrWhiteSpace(label))
            label = dial.Press?.Label;

        button.DisplayText = Fit(label);
    }

    private static void ApplyKey(TouchButton button, Lp5PlannedKey key, Lp5Package package, IAssetService assets,
        int keySizePx, Dictionary<string, string> assetCache)
    {
        if (key.Mapping.IsMapped)
            button.Command = key.Mapping.Command;

        string label = Fit(key.Mapping.Label);
        Lp5Icon icon = package.GetIcon(key.Action);
        string relative = icon == null ? null : ImportIcon(key.Action, icon, assets, assetCache);

        if (!string.IsNullOrEmpty(relative))
        {
            if (icon.IsPreRendered)
            {
                // The image is the whole Loupedeck key, its caption included.
                button.Layers.Add(new ImageLayer { Name = label, AssetRelativePath = relative, Scale = 1.0 });
            }
            else
            {
                // Only a glyph: icon on top, caption underneath.
                button.Layers.Add(new ImageLayer
                {
                    Name = label,
                    AssetRelativePath = relative,
                    Scale = AppAssignment.DefaultIconScaleFor(keySizePx) * 0.8,
                    PositionY = -(keySizePx / 8)
                });
                if (label.Length > 0)
                    button.Layers.Add(LabelLayer(label, keySizePx / 3, keySizePx));
            }
        }
        else if (label.Length > 0)
        {
            button.Layers.Add(LabelLayer(label, 0, keySizePx));
        }

        button.RewireLayerHandlers();
    }

    private static string ImportIcon(string action, Lp5Icon icon, IAssetService assets, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(action, out string cached))
            return cached;

        string relative = null;
        string temp = Path.Combine(Path.GetTempPath(), $"lp5_{Guid.NewGuid():N}.png");
        try
        {
            byte[] png = icon.IsSvg ? RasterizeSvg(icon.Bytes) : icon.Bytes;
            if (png != null)
            {
                File.WriteAllBytes(temp, png);
                relative = assets.Import(temp);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lp5] Icon for '{action}' could not be imported: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }

        cache[action] = relative;
        return relative;
    }

    /// <summary>Renders an SVG glyph to a square PNG; null when the SVG cannot be read.</summary>
    private static byte[] RasterizeSvg(byte[] svgBytes)
    {
        using SKSvg svg = new();
        using MemoryStream input = new(svgBytes);
        SKPicture picture = svg.Load(input);
        if (picture == null)
            return null;

        SKRect bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        float scale = SvgRasterSize / Math.Max(bounds.Width, bounds.Height);
        using SKBitmap bitmap = new(SvgRasterSize, SvgRasterSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate((SvgRasterSize - (bounds.Width * scale)) / 2f, (SvgRasterSize - (bounds.Height * scale)) / 2f);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    private static TextLayer LabelLayer(string label, int positionY, int keySizePx) => new()
    {
        Name = label,
        Text = label,
        Centered = true,
        TextSize = Math.Max(10, keySizePx / 7),
        PositionY = positionY,
        BoxWidth = keySizePx,
        BoxHeight = keySizePx / 2
    };

    private static string Fit(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        // Loupedeck captions use runs of spaces to force line breaks on its own keys.
        string trimmed = string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return trimmed.Length <= MaxLabelLength ? trimmed : trimmed[..MaxLabelLength].TrimEnd();
    }
}
