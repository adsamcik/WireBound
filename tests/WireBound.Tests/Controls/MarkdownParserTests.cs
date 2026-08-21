using AwesomeAssertions;
using WireBound.Avalonia.Controls;

namespace WireBound.Tests.Controls;

public class MarkdownParserTests
{
    [Test]
    public async Task Parse_ReleaseNotes_RecognizesHeadingsAndLists()
    {
        const string markdown = """
            ## WireBound v0.10.0

            ### Added

            - **Unified Resource Dashboard** - Replaced the overview.
            1. First migration step
            """;

        var blocks = MarkdownParser.Parse(markdown);

        blocks.Should().HaveCount(4);
        blocks[0].Should().Be(new MarkdownBlock(MarkdownBlockKind.Heading, "WireBound v0.10.0", 2));
        blocks[1].Should().Be(new MarkdownBlock(MarkdownBlockKind.Heading, "Added", 3));
        blocks[2].Kind.Should().Be(MarkdownBlockKind.UnorderedListItem);
        blocks[2].Text.Should().Be("**Unified Resource Dashboard** - Replaced the overview.");
        blocks[3].Should().Be(new MarkdownBlock(MarkdownBlockKind.OrderedListItem, "First migration step", 0, "1."));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ParseInlines_RemovesSyntaxAndPreservesFormatting()
    {
        const string markdown = "Use **bold**, *italic*, `code`, ~~old~~, and [the changelog](CHANGELOG.md).";

        var inlines = MarkdownParser.ParseInlines(markdown);

        string.Concat(inlines.Select(item => item.Text))
            .Should().Be("Use bold, italic, code, old, and the changelog.");
        inlines.Single(item => item.Text == "bold").IsBold.Should().BeTrue();
        inlines.Single(item => item.Text == "italic").IsItalic.Should().BeTrue();
        inlines.Single(item => item.Text == "code").IsCode.Should().BeTrue();
        inlines.Single(item => item.Text == "old").IsStrikethrough.Should().BeTrue();
        inlines.Single(item => item.Text == "the changelog").Link.Should().Be("CHANGELOG.md");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Parse_CodeQuoteTaskAndRule_RecognizesBlocks()
    {
        const string markdown = """
            > A useful note

            - [x] Completed item

            ---

            ```text
            literal **markdown**
            ```
            """;

        var blocks = MarkdownParser.Parse(markdown);

        blocks.Select(block => block.Kind).Should().ContainInOrder(
            MarkdownBlockKind.Quote,
            MarkdownBlockKind.UnorderedListItem,
            MarkdownBlockKind.Rule,
            MarkdownBlockKind.Code);
        blocks[1].Marker.Should().Be("☑");
        blocks[3].Text.Should().Be("literal **markdown**");

        await Task.CompletedTask;
    }
}
