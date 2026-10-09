using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.Services.Actions;

namespace LoupixDeck.ViewModels;

/// <summary>
/// The layout template choice of the import dialogs: the templates to pick from, the pick, and what it
/// would do to the imported keys, so the import can name that before there is no way back.
/// </summary>
public sealed partial class LayoutTemplatePickerViewModel : ObservableObject
{
    private readonly Func<ButtonTemplate, TemplateApplyResult?> _plan;

    /// <param name="hint">The line under the picker that says what a template does to this kind of import.</param>
    /// <param name="plan">What a template would do to the imported keys; null while that is not known yet.</param>
    public LayoutTemplatePickerViewModel(string hint, Func<ButtonTemplate, TemplateApplyResult?> plan)
    {
        Hint = hint;
        _plan = plan;
        Selected = Templates[0];
    }

    public IReadOnlyList<LayoutTemplateOption> Templates { get; } = LayoutTemplateOption.CreateAll();

    public string Hint { get; }

    [ObservableProperty]
    public partial LayoutTemplateOption Selected { get; set; }

    /// <summary>The picked template; null for none.</summary>
    public ButtonTemplate? Template => Selected?.Template;

    /// <summary>What the picked template will do to the imported keys; empty without a template.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial string Preview { get; set; } = string.Empty;

    public bool HasPreview => !string.IsNullOrEmpty(Preview);

    partial void OnSelectedChanged(LayoutTemplateOption value) => Refresh();

    /// <summary>Plans the picked template again, for when what it is planned on has changed.</summary>
    public void Refresh()
    {
        Preview = Template is { } template && _plan?.Invoke(template) is { } planned
            ? Loc.Tr("ProfileImport_LayoutTemplatePreview", planned.ButtonsChanged, planned.LayersRemoved)
            : string.Empty;
    }
}