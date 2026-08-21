using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace WireBound.Avalonia.Controls;

/// <summary>
/// Renders the subset of GitHub-flavoured Markdown used by WireBound release notes.
/// Keeping this small and native avoids embedding a browser or loading remote content.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<Uri?> LinkBaseUriProperty =
        AvaloniaProperty.Register<MarkdownView, Uri?>(nameof(LinkBaseUri));

    public MarkdownView()
    {
        Spacing = 12;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Uri? LinkBaseUri
    {
        get => GetValue(LinkBaseUriProperty);
        set => SetValue(LinkBaseUriProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty || change.Property == LinkBaseUriProperty)
        {
            RenderMarkdown();
        }
    }

    private void RenderMarkdown()
    {
        Children.Clear();

        foreach (var block in MarkdownParser.Parse(Markdown))
        {
            Children.Add(CreateBlock(block));
        }
    }

    private Control CreateBlock(MarkdownBlock block) => block.Kind switch
    {
        MarkdownBlockKind.Heading => CreateTextBlock(block.Text, $"MarkdownHeading{block.Level}"),
        MarkdownBlockKind.UnorderedListItem => CreateListItem(block, block.Marker ?? "•"),
        MarkdownBlockKind.OrderedListItem => CreateListItem(block, block.Marker ?? "1."),
        MarkdownBlockKind.Quote => CreateQuote(block.Text),
        MarkdownBlockKind.Code => CreateCodeBlock(block.Text),
        MarkdownBlockKind.Rule => new Separator { Classes = { "MarkdownRule" } },
        _ => CreateTextBlock(block.Text, "MarkdownParagraph")
    };

    private TextBlock CreateTextBlock(string text, string styleClass)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Inlines = new InlineCollection()
        };
        textBlock.Classes.Add(styleClass);
        AddInlines(textBlock.Inlines, MarkdownParser.ParseInlines(text));
        return textBlock;
    }

    private Control CreateListItem(MarkdownBlock block, string marker)
    {
        var markerBlock = new TextBlock
        {
            Text = marker,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        markerBlock.Classes.Add("MarkdownListMarker");

        var content = CreateTextBlock(block.Text, "MarkdownListText");
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("24,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(block.Level * 18, 0, 0, 0)
        };
        grid.Classes.Add("MarkdownListItem");
        grid.Children.Add(markerBlock);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private Control CreateQuote(string text)
    {
        var quote = CreateTextBlock(text, "MarkdownQuoteText");
        var border = new Border
        {
            Child = quote,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 4)
        };
        border.Classes.Add("MarkdownQuote");
        return border;
    }

    private static Control CreateCodeBlock(string text)
    {
        var code = new SelectableTextBlock
        {
            Text = text.TrimEnd(),
            TextWrapping = TextWrapping.NoWrap
        };
        code.Classes.Add("MarkdownCodeBlockText");

        var border = new Border { Child = code };
        border.Classes.Add("MarkdownCodeBlock");
        return border;
    }

    private void AddInlines(InlineCollection inlines, IReadOnlyList<MarkdownInline> parsedInlines)
    {
        foreach (var item in parsedInlines)
        {
            if (item.Link is not null && TryResolveLink(item.Link, out var uri))
            {
                var link = new HyperlinkButton
                {
                    Content = item.Text,
                    NavigateUri = uri,
                    FontWeight = item.IsBold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = item.IsItalic ? FontStyle.Italic : FontStyle.Normal
                };
                link.Classes.Add("MarkdownLink");
                inlines.Add(link);
                continue;
            }

            var run = new Run(item.Text)
            {
                FontWeight = item.IsBold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = item.IsItalic ? FontStyle.Italic : FontStyle.Normal,
                TextDecorations = item.IsStrikethrough ? TextDecorations.Strikethrough : null
            };

            if (item.IsCode)
            {
                run.Classes.Add("MarkdownInlineCode");
            }

            inlines.Add(run);
        }
    }

    private bool TryResolveLink(string target, out Uri uri)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out uri!))
        {
            return uri.Scheme is "http" or "https";
        }

        return LinkBaseUri is not null && Uri.TryCreate(LinkBaseUri, target, out uri!);
    }
}

internal static partial class MarkdownParser
{
    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<text>.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-+*])\s+(?<text>.*)$")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>\d+[.)])\s+(?<text>.*)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^\[(?<state>[ xX])\]\s+(?<text>.*)$")]
    private static partial Regex TaskListRegex();

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var blocks = new List<MarkdownBlock>();

        for (var index = 0; index < lines.Length;)
        {
            var line = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (TryGetFence(line, out var fence))
            {
                blocks.Add(ParseCodeBlock(lines, ref index, fence));
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                blocks.Add(new MarkdownBlock(
                    MarkdownBlockKind.Heading,
                    heading.Groups["text"].Value,
                    heading.Groups["hashes"].Value.Length));
                index++;
                continue;
            }

            if (RuleRegex().IsMatch(line))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, string.Empty));
                index++;
                continue;
            }

            if (TryParseListItem(line, out var listItem))
            {
                blocks.Add(listItem);
                index++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                blocks.Add(ParseQuote(lines, ref index));
                continue;
            }

            blocks.Add(ParseParagraph(lines, ref index));
        }

        return blocks;
    }

    public static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var result = new List<MarkdownInline>();
        ParseInlineRange(text, 0, text.Length, default, result);
        return result;
    }

    private static MarkdownBlock ParseCodeBlock(string[] lines, ref int index, string fence)
    {
        index++;
        var code = new StringBuilder();
        while (index < lines.Length && !lines[index].TrimStart().StartsWith(fence, StringComparison.Ordinal))
        {
            if (code.Length > 0)
            {
                code.AppendLine();
            }

            code.Append(lines[index]);
            index++;
        }

        if (index < lines.Length)
        {
            index++;
        }

        return new MarkdownBlock(MarkdownBlockKind.Code, code.ToString());
    }

    private static MarkdownBlock ParseQuote(string[] lines, ref int index)
    {
        var text = new StringBuilder();
        while (index < lines.Length)
        {
            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith('>'))
            {
                break;
            }

            AppendWithSpace(text, trimmed[1..].TrimStart());
            index++;
        }

        return new MarkdownBlock(MarkdownBlockKind.Quote, text.ToString());
    }

    private static MarkdownBlock ParseParagraph(string[] lines, ref int index)
    {
        var text = new StringBuilder();
        while (index < lines.Length)
        {
            var line = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || (text.Length > 0 && StartsNewBlock(line)))
            {
                break;
            }

            AppendWithSpace(text, line.Trim());
            index++;
        }

        return new MarkdownBlock(MarkdownBlockKind.Paragraph, text.ToString());
    }

    private static bool TryParseListItem(string line, out MarkdownBlock block)
    {
        var unordered = UnorderedListRegex().Match(line);
        if (unordered.Success)
        {
            var text = unordered.Groups["text"].Value;
            var marker = "•";
            var task = TaskListRegex().Match(text);
            if (task.Success)
            {
                marker = task.Groups["state"].Value == " " ? "☐" : "☑";
                text = task.Groups["text"].Value;
            }

            block = new MarkdownBlock(
                MarkdownBlockKind.UnorderedListItem,
                text,
                GetIndentLevel(unordered.Groups["indent"].Value),
                marker);
            return true;
        }

        var ordered = OrderedListRegex().Match(line);
        if (ordered.Success)
        {
            block = new MarkdownBlock(
                MarkdownBlockKind.OrderedListItem,
                ordered.Groups["text"].Value,
                GetIndentLevel(ordered.Groups["indent"].Value),
                ordered.Groups["marker"].Value);
            return true;
        }

        block = default;
        return false;
    }

    private static bool StartsNewBlock(string line) =>
        TryGetFence(line, out _) ||
        HeadingRegex().IsMatch(line) ||
        RuleRegex().IsMatch(line) ||
        UnorderedListRegex().IsMatch(line) ||
        OrderedListRegex().IsMatch(line) ||
        line.TrimStart().StartsWith('>');

    private static bool TryGetFence(string line, out string fence)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            fence = "```";
            return true;
        }

        if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            fence = "~~~";
            return true;
        }

        fence = string.Empty;
        return false;
    }

    private static int GetIndentLevel(string indent) =>
        indent.Replace("\t", "    ", StringComparison.Ordinal).Length / 2;

    private static void AppendWithSpace(StringBuilder builder, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(text);
    }

    private static void ParseInlineRange(
        string text,
        int start,
        int end,
        InlineStyle style,
        List<MarkdownInline> output)
    {
        var plain = new StringBuilder();
        var index = start;

        while (index < end)
        {
            if (text[index] == '\\' && index + 1 < end)
            {
                plain.Append(text[index + 1]);
                index += 2;
                continue;
            }

            if (TryReadDelimited(text, index, end, "`", out var contentStart, out var contentEnd, out var next))
            {
                FlushPlain(plain, style, output);
                AddInline(output, text[contentStart..contentEnd], style with { IsCode = true });
                index = next;
                continue;
            }

            if (TryReadLink(text, index, end, out var labelStart, out var labelEnd, out var target, out next))
            {
                FlushPlain(plain, style, output);
                ParseInlineRange(text, labelStart, labelEnd, style with { Link = target }, output);
                index = next;
                continue;
            }

            if (TryReadDelimited(text, index, end, "**", out contentStart, out contentEnd, out next) ||
                TryReadDelimited(text, index, end, "__", out contentStart, out contentEnd, out next))
            {
                FlushPlain(plain, style, output);
                ParseInlineRange(text, contentStart, contentEnd, style with { IsBold = true }, output);
                index = next;
                continue;
            }

            if (TryReadDelimited(text, index, end, "~~", out contentStart, out contentEnd, out next))
            {
                FlushPlain(plain, style, output);
                ParseInlineRange(text, contentStart, contentEnd, style with { IsStrikethrough = true }, output);
                index = next;
                continue;
            }

            if ((text[index] == '*' || text[index] == '_') &&
                TryReadDelimited(text, index, end, text[index].ToString(), out contentStart, out contentEnd, out next))
            {
                FlushPlain(plain, style, output);
                ParseInlineRange(text, contentStart, contentEnd, style with { IsItalic = true }, output);
                index = next;
                continue;
            }

            plain.Append(text[index]);
            index++;
        }

        FlushPlain(plain, style, output);
    }

    private static bool TryReadDelimited(
        string text,
        int index,
        int end,
        string delimiter,
        out int contentStart,
        out int contentEnd,
        out int next)
    {
        contentStart = contentEnd = next = 0;
        if (index + delimiter.Length >= end ||
            !text.AsSpan(index, delimiter.Length).SequenceEqual(delimiter))
        {
            return false;
        }

        var closing = text.IndexOf(delimiter, index + delimiter.Length, StringComparison.Ordinal);
        if (closing < 0 || closing >= end || closing == index + delimiter.Length)
        {
            return false;
        }

        contentStart = index + delimiter.Length;
        contentEnd = closing;
        next = closing + delimiter.Length;
        return true;
    }

    private static bool TryReadLink(
        string text,
        int index,
        int end,
        out int labelStart,
        out int labelEnd,
        out string target,
        out int next)
    {
        labelStart = labelEnd = next = 0;
        target = string.Empty;
        if (text[index] != '[')
        {
            return false;
        }

        var closingBracket = text.IndexOf(']', index + 1);
        if (closingBracket <= index + 1 || closingBracket + 2 >= end || text[closingBracket + 1] != '(')
        {
            return false;
        }

        var closingParenthesis = text.IndexOf(')', closingBracket + 2);
        if (closingParenthesis < 0 || closingParenthesis >= end)
        {
            return false;
        }

        labelStart = index + 1;
        labelEnd = closingBracket;
        target = text[(closingBracket + 2)..closingParenthesis].Trim();
        next = closingParenthesis + 1;
        return target.Length > 0;
    }

    private static void FlushPlain(StringBuilder text, InlineStyle style, List<MarkdownInline> output)
    {
        if (text.Length == 0)
        {
            return;
        }

        AddInline(output, text.ToString(), style);
        text.Clear();
    }

    private static void AddInline(List<MarkdownInline> output, string text, InlineStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        var item = new MarkdownInline(
            text,
            style.IsBold,
            style.IsItalic,
            style.IsStrikethrough,
            style.IsCode,
            style.Link);

        if (output.Count > 0 && output[^1].HasSameStyle(item))
        {
            output[^1] = output[^1] with { Text = output[^1].Text + text };
        }
        else
        {
            output.Add(item);
        }
    }

    private readonly record struct InlineStyle(
        bool IsBold = false,
        bool IsItalic = false,
        bool IsStrikethrough = false,
        bool IsCode = false,
        string? Link = null);
}

internal enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    UnorderedListItem,
    OrderedListItem,
    Quote,
    Code,
    Rule
}

internal readonly record struct MarkdownBlock(
    MarkdownBlockKind Kind,
    string Text,
    int Level = 0,
    string? Marker = null);

internal readonly record struct MarkdownInline(
    string Text,
    bool IsBold,
    bool IsItalic,
    bool IsStrikethrough,
    bool IsCode,
    string? Link)
{
    public bool HasSameStyle(MarkdownInline other) =>
        IsBold == other.IsBold &&
        IsItalic == other.IsItalic &&
        IsStrikethrough == other.IsStrikethrough &&
        IsCode == other.IsCode &&
        Link == other.Link;
}
