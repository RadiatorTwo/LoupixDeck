using System.Collections.ObjectModel;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.IconPacks;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using SkiaSharp;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// Applies a layout template to one touch button with the questions that go with it: the symbol
/// picker when the template needs an icon the button does not have, and a confirmation before
/// layers are removed. Shared by the button editor and the touch button context menu, so both
/// behave the same.
/// </summary>
public interface IButtonTemplateService
{
    /// <summary>
    /// Arranges the icon and caption of <paramref name="button"/>'s active state as
    /// <paramref name="template"/> on a key of the given size. Cancelling the picker or the
    /// confirmation leaves the button as it was. The caller owns saving and repainting.
    /// </summary>
    Task<ButtonTemplateOutcome> ApplyAsync(TouchButton button, ButtonTemplate template, int keyWidthPx,
        int keyHeightPx);

    /// <summary>
    /// Opens the symbol picker and builds a symbol layer for the choice, named so that no layer in
    /// <paramref name="layers"/> has the name yet. Null when the picker was cancelled or the icon
    /// could not be imported. The layer is not added anywhere.
    /// </summary>
    Task<SymbolLayer> PickSymbolLayerAsync(IEnumerable<LayerBase> layers);

    /// <summary>
    /// Puts an icon-pack icon on a symbol layer: the file is copied into the asset store, so the
    /// button keeps working when the pack folder goes away. Colored icons keep their colors, single-
    /// color ones take the tint like a glyph. Returns false when the file could not be copied.
    /// </summary>
    bool ApplyPackIcon(SymbolLayer layer, IconPackEntry icon, double size);
}

/// <summary>What <see cref="IButtonTemplateService.ApplyAsync"/> did.</summary>
/// <param name="Applied">False when nothing changed: cancelled, or nothing to lay out.</param>
/// <param name="AddedIcon">The symbol layer the picker added, or null.</param>
public readonly record struct ButtonTemplateOutcome(bool Applied, SymbolLayer AddedIcon);

/// <inheritdoc cref="IButtonTemplateService"/>
public sealed class ButtonTemplateService(IDialogService dialogService, IAssetService assetService)
    : IButtonTemplateService
{
    private const double PickedSymbolSize = 0.7;

    public async Task<ButtonTemplateOutcome> ApplyAsync(TouchButton button, ButtonTemplate template,
        int keyWidthPx, int keyHeightPx)
    {
        if (button?.Layers == null)
            return default;

        ButtonTemplatePlan plan = ActionAssignment.PlanTemplate(button, template, keyWidthPx, keyHeightPx);

        // A template that needs an icon asks for one first; cancelling the picker changes nothing.
        SymbolLayer addedIcon = null;
        if (template != ButtonTemplate.TextOnly && !plan.HasIcon)
        {
            addedIcon = await PickSymbolLayerAsync(button.Layers);
            if (addedIcon == null)
                return default;

            AddLayer(button.Layers, addedIcon, keyWidthPx, keyHeightPx);
            plan = ActionAssignment.PlanTemplate(button, template, keyWidthPx, keyHeightPx);
        }

        int removed = plan.Removed.Count;
        if (removed > 0)
        {
            DialogResult result = await dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
                vm.Configure(
                    Loc.Tr("Confirm_ApplyTemplateMessage", removed),
                    title: Loc.Tr("Confirm_ApplyTemplateTitle"),
                    confirmText: Loc.Tr("Confirm_Overwrite"),
                    cancelText: Loc.Tr("Confirm_Cancel")));
            if (result is not { IsConfirmed: true })
            {
                if (addedIcon != null)
                    button.Layers.Remove(addedIcon);

                return default;
            }
        }

        ObservableCollection<LayerBase> layers = button.Layers;
        if (!ActionAssignment.ApplyTemplate(button, plan, name => UniqueLayerName(layers, name)))
            return default;

        return new ButtonTemplateOutcome(true, addedIcon);
    }

    public async Task<SymbolLayer> PickSymbolLayerAsync(IEnumerable<LayerBase> layers)
    {
        SymbolPickerRequest request = new();
        DialogResult result = await dialogService.ShowDialogAsync<SymbolPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (result is not { IsConfirmed: true }) return null;

        SymbolLayer layer;
        if (request.SelectedPackIcon is { } icon)
        {
            layer = new SymbolLayer { Name = UniqueLayerName(layers, icon.DisplayName) };
            if (!ApplyPackIcon(layer, icon, PickedSymbolSize)) return null;
        }
        else if (request.SelectedSymbol is { } def)
        {
            layer = new SymbolLayer
            {
                Name = UniqueLayerName(layers, def.DisplayName),
                SymbolId = def.Id
            };
            layer.FitScaleToGlyph(PickedSymbolSize);
        }
        else
            return null;

        return layer;
    }

    public bool ApplyPackIcon(SymbolLayer layer, IconPackEntry icon, double size)
    {
        string relative;
        try
        {
            relative = assetService.Import(icon.FullPath, "icons");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[IconPacks] Importing '{icon.FullPath}' failed: {ex.Message}");
            return false;
        }

        if (string.IsNullOrEmpty(relative)) return false;

        SKBitmap bitmap = assetService.Load(relative);
        SKRectI bounds = bitmap != null ? IconColorAnalysis.GetContentBounds(bitmap) : SKRectI.Empty;

        layer.SymbolId = string.Empty;
        layer.IconSource = icon.Key;
        layer.IconAssetPath = relative;
        layer.KeepOriginalColors = bitmap != null && !IconColorAnalysis.IsMonochrome(bitmap);
        layer.FitScaleToAspect(size, bounds.Height > 0 ? (double)bounds.Width / bounds.Height : 1.0);
        return true;
    }

    /// <summary>
    /// Returns <paramref name="baseName"/>, or the first "<paramref name="baseName"/> N" that no layer
    /// has yet ("Text" → "Text 1" → "Text 2" …).
    /// </summary>
    public static string UniqueLayerName(IEnumerable<LayerBase> layers, string baseName)
    {
        if (layers == null)
            return baseName;

        bool Exists(string name) =>
            layers.Any(l => string.Equals(l?.Name, name, StringComparison.Ordinal));

        if (!Exists(baseName))
            return baseName;

        int index = 1;
        while (Exists($"{baseName} {index}"))
            index++;

        return $"{baseName} {index}";
    }

    /// <summary>Adds a new layer, stamped with the surface size like the layers the editor shows.</summary>
    private static void AddLayer(ObservableCollection<LayerBase> layers, LayerBase layer, int keyWidthPx,
        int keyHeightPx)
    {
        layer.DeviceBaseWidth = keyWidthPx;
        layer.DeviceBaseHeight = keyHeightPx;
        layers.Add(layer);
    }
}
