using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Localization;

/// <summary>
/// XAML markup extension that yields the translated string for a key and keeps it live:
/// <c>Text="{loc:Tr About_Close}"</c>. It binds one-way to the <see cref="LocalizationManager"/>
/// indexer, so when the language changes the bound text updates in place, with no window reload.
///
/// Keys are stable identifiers (letters, digits and underscores). The human-readable source text
/// lives in the JSON dictionaries, not in the XAML, which keeps the markup free of the commas and
/// apostrophes that would otherwise trip the markup-extension parser.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding
        {
            Source = LocalizationManager.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay
        };
    }
}
