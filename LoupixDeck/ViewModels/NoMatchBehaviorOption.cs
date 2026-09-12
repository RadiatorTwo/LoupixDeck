using LoupixDeck.Localization;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One entry of the Profile Rules "when leaving matched apps" dropdown. The option carries the
/// translation key rather than the text, and exposes the live <see cref="TranslatedString"/> the
/// item template binds, so the open dropdown follows a language change like the rest of the UI.
/// </summary>
public sealed record NoMatchBehaviorOption(NoMatchProfileBehavior Value, string LabelKey)
{
    public TranslatedString Label => LocalizationManager.Instance.Entry(LabelKey);
}
