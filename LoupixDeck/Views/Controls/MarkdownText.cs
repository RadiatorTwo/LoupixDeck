using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Read-only view of the Markdown GitHub release notes are written in: headings, bullet and
/// numbered lists (nested by indentation), code blocks, rules and paragraphs, with bold, italic,
/// inline code and links inside them. Not a full Markdown implementation - anything it does not
/// know stays plain text, so a note is never lost, only less formatted.
/// Body text takes the inherited foreground; headings use AppTextPrimary.
/// </summary>
public sealed partial class MarkdownText : StackPanel
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownText, string>(nameof(Markdown));

    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownText()
    {
        Spacing = 6;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            Rebuild(Markdown);
        }
    }

    private void Rebuild(string markdown)
    {
        Children.Clear();
        string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        StringBuilder paragraph = new();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                StringBuilder code = new();
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[i]);
                }

                Children.Add(CreateCodeBlock(code.ToString().TrimEnd()));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph(paragraph);
                continue;
            }

            Match heading = HeadingPattern().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph(paragraph);
                Children.Add(CreateHeading(heading.Groups[2].Value, heading.Groups[1].Value.Length));
                continue;
            }

            if (RulePattern().IsMatch(trimmed))
            {
                FlushParagraph(paragraph);
                Children.Add(new Separator { Margin = new Thickness(0, 4) });
                continue;
            }

            Match item = ListItemPattern().Match(line);
            if (item.Success)
            {
                FlushParagraph(paragraph);
                int level = ExpandTabs(item.Groups[1].Value).Length / 2;
                string marker = char.IsDigit(item.Groups[2].Value[0]) ? item.Groups[2].Value : "•";
                Children.Add(CreateListItem(marker, item.Groups[3].Value, level));
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph(paragraph);
    }

    private void FlushParagraph(StringBuilder paragraph)
    {
        if (paragraph.Length == 0)
        {
            return;
        }

        Children.Add(CreateText(paragraph.ToString()));
        paragraph.Clear();
    }

    private static SelectableTextBlock CreateText(string text)
    {
        SelectableTextBlock block = new() { TextWrapping = TextWrapping.Wrap };
        AddInlines(block.Inlines!, text);
        return block;
    }

    private static SelectableTextBlock CreateHeading(string text, int level)
    {
        SelectableTextBlock block = CreateText(text);
        block.FontWeight = FontWeight.SemiBold;
        block.FontSize = level switch
        {
            1 => 17,
            2 => 15,
            _ => 14
        };
        block.Margin = new Thickness(0, 6, 0, 0);
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("AppTextPrimary"));
        return block;
    }

    private static Grid CreateListItem(string marker, string text, int level)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(level * 16, 0, 0, 0)
        };

        TextBlock bullet = new()
        {
            Text = marker,
            MinWidth = 16,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        SelectableTextBlock body = CreateText(text);
        Grid.SetColumn(body, 1);

        grid.Children.Add(bullet);
        grid.Children.Add(body);
        return grid;
    }

    private static Border CreateCodeBlock(string code)
    {
        Border border = new()
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new SelectableTextBlock
            {
                Text = code,
                FontFamily = MonospaceFont,
                TextWrapping = TextWrapping.Wrap
            }
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("AppSurfaceBar"));
        return border;
    }

    /// <summary>Bold (<c>**x**</c>, <c>__x__</c>), italic (<c>*x*</c>), inline code and links as their text.</summary>
    private static void AddInlines(InlineCollection inlines, string text)
    {
        int position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
            {
                inlines.Add(new Run(text[position..match.Index]));
            }

            if (match.Groups["code"].Success)
            {
                inlines.Add(new Run(match.Groups["code"].Value) { FontFamily = MonospaceFont });
            }
            else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
            {
                string inner = match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value;
                Span bold = new() { FontWeight = FontWeight.SemiBold };
                AddInlines(bold.Inlines, inner);
                inlines.Add(bold);
            }
            else if (match.Groups["italic"].Success)
            {
                Span italic = new() { FontStyle = FontStyle.Italic };
                AddInlines(italic.Inlines, match.Groups["italic"].Value);
                inlines.Add(italic);
            }
            else if (match.Groups["link"].Success)
            {
                Span link = new() { TextDecorations = TextDecorations.Underline };
                AddInlines(link.Inlines, match.Groups["link"].Value);
                inlines.Add(link);
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            inlines.Add(new Run(text[position..]));
        }
    }

    private static string ExpandTabs(string indent)
    {
        return indent.Replace("\t", "    ");
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"^(\s*)([*+-]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|__(?<bold2>.+?)__|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?!\w)|\[(?<link>[^\]]+)\]\([^)]+\)")]
    private static partial Regex InlinePattern();
}
