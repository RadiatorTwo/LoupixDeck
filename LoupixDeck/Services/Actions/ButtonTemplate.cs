using LoupixDeck.Models.Layers;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// How the icon and the caption of a touch button are arranged on the key; see
/// <see cref="ActionAssignment.ApplyTemplate"/>.
/// </summary>
public enum ButtonTemplate
{
    IconCaptionBottom = 0,
    IconCaptionTop = 1,
    IconOnly = 2,
    TextOnly = 3
}

/// <summary>
/// What a template does to one button, worked out by <see cref="ActionAssignment.PlanTemplate"/> and
/// carried out by <see cref="ActionAssignment.ApplyTemplate"/>.
/// </summary>
public sealed class ButtonTemplatePlan
{
    /// <summary>False when the template does nothing: icon only on a button with no icon.</summary>
    public bool Applies { get; init; }

    /// <summary>Whether the button has an icon layer a template can use.</summary>
    public bool HasIcon { get; init; }

    /// <summary>The layers applying removes from the button.</summary>
    public IReadOnlyList<LayerBase> Removed { get; init; } = [];

    /// <summary>The layout the button gets, which is not the chosen template when a layer it needs is missing or one it drops has to stay.</summary>
    internal ButtonTemplate Layout { get; init; }

    internal LayerBase Icon { get; init; }

    /// <summary>Null when the layout has a caption and the button has none yet.</summary>
    internal TextLayer Caption { get; init; }

    internal int KeyWidthPx { get; init; }

    internal int KeyHeightPx { get; init; }
}

/// <summary>What <see cref="ActionAssignment.ApplyTemplateToPages"/> did: buttons with at least one state laid out, and the layers dropped.</summary>
public readonly record struct TemplateApplyResult(int ButtonsChanged, int LayersRemoved);
