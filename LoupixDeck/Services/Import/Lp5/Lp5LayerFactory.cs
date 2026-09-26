using System.Security.Cryptography;
using System.Text;
using LoupixDeck.Localization;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Builds key layers from a Loupedeck action's artwork: the icon description (<c>.ict</c>) when there
/// is one, otherwise the pre-rendered key image, otherwise a caption.
/// </summary>
/// <remarks>
/// Loupedeck places icon items on a 0..100 grid over the key; those units are scaled to the device's
/// key size. SVG icons are stored as SVG assets and rendered by the asset store. With no asset store
/// (a preview pass) nothing is written and image layers carry no asset path. Ported from
/// <c>lp5_to_loupix.py</c> (loupedeck-to-loupixdeck by Vencite, MIT license).
/// </remarks>
internal sealed class Lp5LayerFactory(Lp5Archive archive, IAssetService assets, int keySize)
{
    private const int MaxCaptionLength = 28;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Dictionary<string, string> _stored = new(StringComparer.Ordinal);

    /// <summary>Image layers created so far.</summary>
    public int Icons { get; private set; }

    /// <summary>Icon images that could not be read and were left out.</summary>
    public int UnreadableIcons { get; private set; }

    /// <summary>Loupedeck grid units (0..100 across a key) to device pixels.</summary>
    private double Unit => keySize / 100.0;

    /// <summary>The layers for an action's key: icon, key image or caption, in that order.</summary>
    public List<LayerBase> ButtonLayers(string actionRef, string label)
    {
        if (Lp5ActionResolver.IsNone(actionRef)) return [];

        List<LayerBase> layers = IconLayers(actionRef);
        if (layers.Count > 0) return layers;

        byte[] image = archive.FindImage(actionRef);
        if (image != null)
        {
            Icons++;
            return [new ImageLayer
            {
                Name = string.IsNullOrEmpty(label) ? Loc.Tr("LoupedeckImport_LayerIcon") : label,
                AssetRelativePath = Store(image, ".png"),
                Scale = 1.0
            }];
        }

        return string.IsNullOrEmpty(label) ? [] : [Caption(label)];
    }

    /// <summary>A centred caption, shortened to what fits on a key.</summary>
    public static TextLayer Caption(string text, string name = null)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length > MaxCaptionLength)
            text = text[..(MaxCaptionLength - 1)] + "…";

        return new TextLayer
        {
            Name = name ?? Loc.Tr("LoupedeckImport_LayerLabel"),
            Text = text,
            TextSize = text.Length > 12 ? 13 : 16,
            Centered = true
        };
    }

    private List<LayerBase> IconLayers(string actionRef)
    {
        JObject icon = archive.FindIcon(actionRef);
        if (icon == null) return [];

        List<LayerBase> layers = [];
        foreach (JToken item in Lp5Json.Arr(icon, "items"))
        {
            if (!Lp5Json.Bool(item, "isVisible")) continue;

            JObject area = Lp5Json.Obj(item, "area");
            double x = Lp5Json.Num(area, "x", 0);
            double y = Lp5Json.Num(area, "y", 0);
            double width = Lp5Json.Num(area, "width", 100);
            double height = Lp5Json.Num(area, "height", 100);
            int centerX = (int)Math.Round((x + (width / 2) - 50) * Unit);
            int centerY = (int)Math.Round((y + (height / 2) - 50) * Unit);

            string type = Lp5Json.Str(item, "itemType");
            if (type == "Image" && Lp5Json.Str(item, "image") is { Length: > 0 } encoded)
            {
                string relative = StoreIcon(encoded, out bool readable);
                if (!readable)
                {
                    UnreadableIcons++;
                    continue;
                }

                Icons++;
                layers.Add(new ImageLayer
                {
                    Name = Loc.Tr("LoupedeckImport_LayerIcon"),
                    AssetRelativePath = relative,
                    Scale = width / 100,
                    PositionX = centerX,
                    PositionY = centerY
                });
            }
            else if (type == "Text" && Lp5Json.Str(item, "text") is { Length: > 0 } text)
            {
                TextLayer caption = Caption(text);
                caption.BoxWidth = (int)Math.Round(width * Unit);
                caption.BoxHeight = (int)Math.Round(height * Unit);
                caption.PositionX = centerX;
                caption.PositionY = centerY;
                caption.TextSize = Math.Max(8, (int)Lp5Json.Num(item, "fontSize", 8));
                layers.Add(caption);
            }
        }

        return layers;
    }

    /// <summary>Stores a base64 icon image as PNG, JPEG or SVG asset.</summary>
    private string StoreIcon(string encoded, out bool readable)
    {
        readable = false;
        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        string extension = DetectExtension(data);
        if (extension == null) return null;

        readable = true;
        return Store(data, extension);
    }

    private static string DetectExtension(byte[] data)
    {
        if (data.AsSpan().StartsWith(PngSignature)) return ".png";
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";

        // SVG, with or without an XML prolog in front of the root element.
        string head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 1024)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                head.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            ? ".svg"
            : null;
    }

    /// <summary>Puts bytes into the content-addressed asset store; null in a preview pass.</summary>
    private string Store(byte[] data, string extension)
    {
        if (assets == null) return null;

        string key = Convert.ToHexString(SHA256.HashData(data)) + extension;
        if (_stored.TryGetValue(key, out string cached)) return cached;

        // The store imports from a file and keeps its extension, which tells it how to decode.
        string temp = Path.Combine(Path.GetTempPath(), $"lp5_{Guid.NewGuid():N}{extension}");
        string relative = null;
        try
        {
            File.WriteAllBytes(temp, data);
            relative = assets.Import(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[lp5] Could not store an icon: {ex.Message}");
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

        _stored[key] = relative;
        return relative;
    }
}
