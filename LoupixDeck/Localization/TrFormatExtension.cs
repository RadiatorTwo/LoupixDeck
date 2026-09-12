using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Localization;

/// <summary>
/// XAML markup extension for a translated string with a bound value spliced into it, replacing
/// <c>StringFormat</c>, whose template is a literal and therefore cannot be translated:
/// <c>Text="{loc:TrFormat TouchButton_SelectedStateFmt, SelectedState.Name}"</c>.
///
/// It binds the key's <see cref="TranslatedString"/> entry alongside the data-bound value, so the
/// text re-formats both when the value changes and when the language changes.
/// </summary>
public sealed class TrFormatExtension : MarkupExtension
{
    public TrFormatExtension()
    {
    }

    public TrFormatExtension(string key, string path)
    {
        Key = key;
        Path = path;
    }

    /// <summary>Translation key whose value is a composite format string, e.g. <c>"{0} commands"</c>.</summary>
    public string Key { get; set; }

    /// <summary>Binding path, relative to the target's data context, of the value to splice in.</summary>
    public string Path { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = TemplateConverter.Instance,
            Bindings =
            {
                new Binding
                {
                    Source = LocalizationManager.Instance.Entry(Key),
                    Path = nameof(TranslatedString.Value)
                },
                new Binding { Path = Path }
            }
        };
    }

    /// <summary>Fills the translated template (first value) with the bound value (second).</summary>
    private sealed class TemplateConverter : IMultiValueConverter
    {
        public static readonly TemplateConverter Instance = new();

        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[0] is not string template)
            {
                return null;
            }

            try
            {
                return string.Format(culture, template, values[1]);
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"[Localization] Template is not a valid composite format string: {ex.Message}");
                return template;
            }
        }
    }
}
