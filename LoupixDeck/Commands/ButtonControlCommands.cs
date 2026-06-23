using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

/// <summary>
/// Updates a Touch button on the currently active page at runtime. Designed
/// for CLI / scripting use (build status, sensor readouts, now-playing). The
/// changes are in-memory only — they don't persist across app restarts so a
/// busy script can hammer the command without thrashing config.json.
/// </summary>
/// <remarks>
/// Fork-CLI parity: the short form <c>loupixdeck updatebutton 0 text=Hi</c>
/// is rewritten to the full form by the alias in <see cref="Program.CommandChannel"/>.
/// <c>text=Hello_World</c> still becomes "Hello World" and
/// <c>image=clear|null|</c> all clear the image.
/// </remarks>
[Command("System.UpdateButton", "Update Touch Button at runtime", "Button Control",
    parameterTemplate: "({Index},text=,textColor=,backColor=,image=)",
    parameterNames: ["Index", "Properties"],
    parameterTypes: [typeof(int), typeof(string)])]
public class UpdateButtonCommand(IDeviceController controller, IAssetService assetService) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters == null || parameters.Length < 2)
        {
            Console.WriteLine("Usage: System.UpdateButton(index,[layer=name,]text=...,textColor=...,backColor=...,image=...)");
            return Task.CompletedTask;
        }

        if (!int.TryParse(parameters[0], out var buttonIndex))
        {
            Console.WriteLine($"[UpdateButton] invalid index '{parameters[0]}'");
            return Task.CompletedTask;
        }

        var page = controller.Config.CurrentTouchButtonPage;
        var buttons = page?.TouchButtons;
        if (buttons == null || buttonIndex < 0 || buttonIndex >= buttons.Count)
        {
            Console.WriteLine($"[UpdateButton] index {buttonIndex} out of range (page has {buttons?.Count ?? 0} buttons)");
            return Task.CompletedTask;
        }

        var button = buttons.FindByIndex(buttonIndex);
        if (button == null)
        {
            Console.WriteLine($"[UpdateButton] no button at index {buttonIndex}");
            return Task.CompletedTask;
        }

        // First pass: extract layer=<name> so subsequent property updates can
        // target it. Default null = "first of correct type".
        string layerName = null;
        var assignments = new List<(string Key, string Value)>(parameters.Length - 1);
        for (var i = 1; i < parameters.Length; i++)
        {
            if (TrySplitKeyValue(layerName, out var key, out var value))
            {
                if (key == "layer")
                {
                    layerName = value;
                    continue;
                }
                assignments.Add((key, value));
            }
        }

        foreach (var (key, value) in assignments)
        {
            switch (key)
            {
                case "text":
                {
                    var tl = ResolveTextLayer(button, layerName);
                    if (tl == null) break;
                    // Fork-CLI quality-of-life: underscores stand in for spaces
                    // because the short form is space-split.
                    tl.Text = value?.Replace("_", " ") ?? string.Empty;
                    break;
                }
                case "textcolor":
                {
                    if (!TryParseColor(value, out var color))
                    {
                        Console.WriteLine($"[UpdateButton] unparseable textColor '{value}'");
                        break;
                    }
                    var tl = ResolveTextLayer(button, layerName);
                    if (tl == null) break;
                    tl.TextColor = color;
                    break;
                }
                case "backcolor":
                case "backgroundcolor":
                {
                    if (!TryParseColor(value, out var color))
                    {
                        Console.WriteLine($"[UpdateButton] unparseable backColor '{value}'");
                        break;
                    }
                    button.BackColor = color;
                    break;
                }
                case "image":
                {
                    var il = ResolveImageLayer(button, layerName);
                    if (il == null) break;
                    if (string.IsNullOrEmpty(value) ||
                        value.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        il.AssetRelativePath = string.Empty;
                        break;
                    }
                    if (!File.Exists(value))
                    {
                        Console.WriteLine($"[UpdateButton] image file not found: {value}");
                        break;
                    }
                    try
                    {
                        il.AssetRelativePath = assetService.Import(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateButton] image import failed: {ex.Message}");
                    }
                    break;
                }
                default:
                    Console.WriteLine($"[UpdateButton] unknown property '{key}'");
                    break;
            }
        }

        return Task.CompletedTask;
    }

#nullable enable
    private static bool TrySplitKeyValue(string param, [NotNullWhen(true), MaybeNullWhen(false)] out string? key, [NotNullWhen(true), MaybeNullWhen(false)] out string? value)
    {
        if (string.IsNullOrWhiteSpace(param))
        {
            key = value = null;
            return false;
        }
        var split = param.Split('=', 2, StringSplitOptions.TrimEntries);
        if (split.Length != 2)
        {
            Console.WriteLine($"[UpdateButton] expected key=value, got '{param}'");
            key = value = null;
            return false;
        }
        key = split[0].ToLowerInvariant();
        value = split[1];
        return true;
    }
#nullable restore

    /// <summary>
    /// Resolves the TextLayer to write to. With an explicit name, look for an
    /// exact match (case-insensitive); if none is found, create a new layer
    /// with that name rather than silently mutating some other layer. Without
    /// a name, take the first TextLayer (or auto-create with the default name).
    /// </summary>
    private static TextLayer ResolveTextLayer(TouchButton button, string nameHint)
    {
        if (!string.IsNullOrEmpty(nameHint))
        {
            foreach (var layer in button.Layers)
            {
                if (layer is TextLayer tl && string.Equals(tl.Name, nameHint, StringComparison.OrdinalIgnoreCase))
                    return tl;
            }
        }
        else
        {
            foreach (var layer in button.Layers)
                if (layer is TextLayer tl) return tl;
        }

        var created = new TextLayer
        {
            Name = string.IsNullOrEmpty(nameHint) ? "Text" : nameHint,
            BoxWidth = 90,
            BoxHeight = 90
        };
        button.Layers.Add(created);
        return created;
    }

    /// <summary>
    /// Resolves the ImageLayer to write to. Same name semantics as
    /// <see cref="ResolveTextLayer"/>.
    /// </summary>
    private static ImageLayer ResolveImageLayer(TouchButton button, string nameHint)
    {
        if (!string.IsNullOrEmpty(nameHint))
        {
            foreach (var layer in button.Layers)
            {
                if (layer is ImageLayer il && string.Equals(il.Name, nameHint, StringComparison.OrdinalIgnoreCase))
                    return il;
            }
        }
        else
        {
            foreach (var layer in button.Layers)
                if (layer is ImageLayer il) return il;
        }

        var created = new ImageLayer { Name = string.IsNullOrEmpty(nameHint) ? "Image" : nameHint };
        // Insert above any existing image layers but below text/symbol layers,
        // so a freshly added image doesn't cover existing text.
        var insertAt = 0;
        for (var i = 0; i < button.Layers.Count; i++)
            if (button.Layers[i] is ImageLayer) insertAt = i + 1;
        button.Layers.Insert(insertAt, created);
        return created;
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            if (value.StartsWith('#'))
            {
                color = Color.Parse(value);
                return true;
            }
            // Named color via reflection on Avalonia.Media.Colors (case-insensitive).
            var prop = typeof(Colors).GetProperty(value,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.IgnoreCase);
            if (prop?.GetValue(null) is Color c)
            {
                color = c;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Removes a named layer from a Touch button on the currently active page.
/// No-op (with a console warning) when no layer of that exact name exists —
/// scripts can safely call this without first checking.
/// </summary>
/// <remarks>
/// Fork-CLI parity: <c>loupixdeck removelayer 6 MyImage</c>.
/// </remarks>
[Command("System.RemoveLayer", "Remove a Layer from a Touch Button", "Button Control",
    parameterTemplate: "({Index},{Name})",
    parameterNames: ["Index", "Layer Name"],
    parameterTypes: [typeof(int), typeof(string)])]
public class RemoveLayerCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters == null || parameters.Length < 2)
        {
            Console.WriteLine("Usage: System.RemoveLayer(index,layerName)");
            return Task.CompletedTask;
        }

        if (!int.TryParse(parameters[0], out var buttonIndex))
        {
            Console.WriteLine($"[RemoveLayer] invalid index '{parameters[0]}'");
            return Task.CompletedTask;
        }

        var name = parameters[1]?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Console.WriteLine("[RemoveLayer] empty layer name");
            return Task.CompletedTask;
        }

        var buttons = controller.Config.CurrentTouchButtonPage?.TouchButtons;
        if (buttons == null || buttonIndex < 0 || buttonIndex >= buttons.Count)
        {
            Console.WriteLine($"[RemoveLayer] index {buttonIndex} out of range (page has {buttons?.Count ?? 0} buttons)");
            return Task.CompletedTask;
        }

        var button = buttons.FindByIndex(buttonIndex);
        if (button?.Layers == null)
        {
            Console.WriteLine($"[RemoveLayer] no button at index {buttonIndex}");
            return Task.CompletedTask;
        }

        LayerBase match = null;
        foreach (var layer in button.Layers)
        {
            if (layer != null && string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                match = layer;
                break;
            }
        }

        if (match == null)
        {
            Console.WriteLine($"[RemoveLayer] no layer named '{name}' on button {buttonIndex}");
            return Task.CompletedTask;
        }

        button.Layers.Remove(match);
        return Task.CompletedTask;
    }
}
