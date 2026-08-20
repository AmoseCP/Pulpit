using System.Collections.Generic;
using System.Linq;
using Pulpit.Core.Config;
using Pulpit.Core.Content;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>多行歌词模式（P2-2）的分页规则。</summary>
public sealed class LyricsTests
{
    private const string TwoStanzas = """
        奇异恩典 何等甘甜
        我罪已得赦免

        前我失丧 今被寻回
        瞎眼今得看见
        """;

    // ---------------- 空行即分页点 ----------------

    /// <summary>
    /// 歌词天然分小节，而小节长短不一。作词人写下的空行就是「这里该翻页」，
    /// 比任何固定行数都准。
    /// </summary>
    [Fact]
    public void BlankLineStartsANewPage()
    {
        DisplayContent content = ContentBuilder.FromLyrics(TwoStanzas);

        Assert.Equal(ContentKind.Lyrics, content.Kind);
        Assert.Equal(2, content.PageCount);
        Assert.Equal("奇异恩典 何等甘甜\n我罪已得赦免", content.Pages[0].Body);
        Assert.Equal("前我失丧 今被寻回\n瞎眼今得看见", content.Pages[1].Body);
    }

    [Fact]
    public void ConsecutiveBlankLinesCountAsOneBreak()
    {
        DisplayContent content = ContentBuilder.FromLyrics("第一节\n\n\n\n第二节");

        Assert.Equal(2, content.PageCount);
    }

    [Fact]
    public void LeadingAndTrailingBlankLinesProduceNoEmptyPages()
    {
        DisplayContent content = ContentBuilder.FromLyrics("\n\n第一节\n\n");

        Assert.Single(content.Pages);
        Assert.Equal("第一节", content.Pages[0].Body);
    }

    // ---------------- 换行符 ----------------

    /// <summary>
    /// ⚠ 这条是本文件最该有的用例：Windows 的 <c>\r\n</c> 若被当成两个分隔符，
    /// 中间会切出一个空串、被当成空行，于是**每一行都单独成页**。
    /// </summary>
    [Theory]
    [InlineData("第一行\n第二行")]
    [InlineData("第一行\r\n第二行")]
    [InlineData("第一行\r第二行")]
    public void AllLineEndingStylesGiveOnePageWithTwoLines(string text)
    {
        DisplayContent content = ContentBuilder.FromLyrics(text);

        Assert.Single(content.Pages);
        Assert.Equal("第一行\n第二行", content.Pages[0].Body);
    }

    [Fact]
    public void CrLfBlankLineStillBreaksThePage()
    {
        DisplayContent content = ContentBuilder.FromLyrics("第一节\r\n\r\n第二节");

        Assert.Equal(2, content.PageCount);
    }

    // ---------------- 超长小节被切开 ----------------

    [Fact]
    public void LongStanzaIsChunkedByMaxLinesPerPage()
    {
        string stanza = string.Join('\n', Enumerable.Range(1, 9).Select(i => $"第{i}行"));

        DisplayContent content = ContentBuilder.FromLyrics(stanza, maxLinesPerPage: 4);

        Assert.Equal(3, content.PageCount);          // 4 + 4 + 1
        Assert.Equal(4, content.Pages[0].Body.Split('\n').Length);
        Assert.Equal(4, content.Pages[1].Body.Split('\n').Length);
        Assert.Single(content.Pages[2].Body.Split('\n'));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    public void MaxLinesPerPageControlsChunking(int maxLines, int expectedPages)
    {
        string stanza = "一\n二\n三\n四";

        Assert.Equal(expectedPages, ContentBuilder.FromLyrics(stanza, maxLines).PageCount);
    }

    [Fact]
    public void MaxLinesBelowOneIsTreatedAsOne()
        => Assert.Equal(3, ContentBuilder.FromLyrics("一\n二\n三", maxLinesPerPage: 0).PageCount);

    // ---------------- 行内内容 ----------------

    /// <summary>缩进是排版的一部分，不该被吞掉；只去行尾空白（编辑器留下的噪音）。</summary>
    [Fact]
    public void LeadingWhitespaceIsPreservedTrailingIsTrimmed()
    {
        DisplayContent content = ContentBuilder.FromLyrics("  缩进的一行   \n另一行");

        Assert.Equal("  缩进的一行\n另一行", content.Pages[0].Body);
    }

    /// <summary>歌词没有出处标签。</summary>
    [Fact]
    public void LyricsPagesHaveNoLabel()
    {
        DisplayContent content = ContentBuilder.FromLyrics(TwoStanzas);

        Assert.All(content.Pages, page => Assert.Equal(string.Empty, page.Label));
        Assert.Empty(content.Sources);
        Assert.Null(content.Source);
    }

    [Fact]
    public void MultiPageLyricsShowThePageIndicator()
    {
        DisplayContent content = ContentBuilder.FromLyrics(TwoStanzas);

        Assert.Equal("1/2", content.PageIndicator);
        Assert.True(content.TryNext());
        Assert.Equal("2/2", content.PageIndicator);
        Assert.False(content.TryNext());       // 不循环
    }

    // ---------------- 空输入 ----------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\n")]
    public void BlankLyricsProduceNoPages(string text)
    {
        DisplayContent content = ContentBuilder.FromLyrics(text);

        Assert.True(content.IsEmpty);
        Assert.Null(content.Current);
    }

    [Fact]
    public void NullLyricsProduceNoPages()
        => Assert.True(ContentBuilder.FromLyrics(null).IsEmpty);

    // ---------------- 配置 ----------------

    [Fact]
    public void LyricsConfigDefaultsToFourLines()
        => Assert.Equal(4, new AppConfig().Lyrics.LinesPerPage);

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-3)]
    public void OutOfRangeLinesPerPageFallsBackToFour(int value)
    {
        var config = new AppConfig { Lyrics = new LyricsConfig { LinesPerPage = value } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(4, sanitized.Lyrics.LinesPerPage);
        Assert.Single(corrections);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void InRangeLinesPerPageIsKept(int value)
    {
        var config = new AppConfig { Lyrics = new LyricsConfig { LinesPerPage = value } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(value, sanitized.Lyrics.LinesPerPage);
        Assert.Empty(corrections);
    }
}
