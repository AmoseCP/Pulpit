using System.Collections.Generic;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Xunit;

namespace Pulpit.Core.Tests;

[Collection(BibleCollection.Name)]
public sealed class ContentTests
{
    private readonly BibleFixture _fx;

    public ContentTests(BibleFixture fixture) => _fx = fixture;

    private DisplayContent Build(string input, bool useRawText = false)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error),
            $"「{input}」应解析成功，实际 error={error ?? "(无)"}");

        IReadOnlyList<VerseText> verses = _fx.Repository.Lookup(reference!);
        return ContentBuilder.FromVerses(reference!, verses, useRawText);
    }

    // ---------------- 分页（§6 + M4 验收）----------------

    [Fact]
    public void Psalm23_1To6_ProducesSixPages()
    {
        DisplayContent content = Build("诗23:1-6");

        Assert.Equal(ContentKind.Scripture, content.Kind);
        Assert.Equal(6, content.PageCount);
        Assert.True(content.HasMultiplePages);
        Assert.Equal("1/6", content.PageIndicator);
    }

    /// <summary>⚠ 并节去重：诗8:6-8 是一组，只出 1 页，出处标签是整个范围。</summary>
    [Fact]
    public void Psalm8_6To8_ProducesOnePageLabelledAsTheWholeGroup()
    {
        DisplayContent content = Build("诗8:6-8");

        Page page = Assert.Single(content.Pages);
        Assert.Equal("诗篇 8:6-8", page.Label);
        Assert.False(content.HasMultiplePages);

        // 单页不显示页码指示器（M2）。
        Assert.Equal(string.Empty, content.PageIndicator);
    }

    [Fact]
    public void Numbers1_20To21_ProducesOnePage()
        => Assert.Single(Build("民1:20-21").Pages);

    // ---------------- 翻页不循环（M4 验收标准）----------------

    /// <summary>
    /// M4 验收：<c>诗23:1-6</c> 分 6 页，F8 逐页前进，**末页再按 F8 无动作（不循环）**。
    /// 循环会让操作员以为翻页失灵而反复按，这在直播中是可见的抖动。
    /// </summary>
    [Fact]
    public void NextPageStopsAtLastPageAndDoesNotWrap()
    {
        DisplayContent content = Build("诗23:1-6");

        for (int i = 0; i < 5; i++)
        {
            Assert.True(content.TryNext(), $"从第 {i + 1} 页应能前进");
        }

        Assert.Equal(5, content.Index);          // 0-based，即第 6 页
        Assert.Equal("6/6", content.PageIndicator);

        Assert.False(content.TryNext());         // 末页再按，无动作
        Assert.Equal(5, content.Index);          // 索引不动，没有回到第 1 页
    }

    [Fact]
    public void PreviousPageStopsAtFirstPageAndDoesNotWrap()
    {
        DisplayContent content = Build("诗23:1-6");

        Assert.Equal(0, content.Index);
        Assert.False(content.TryPrevious());
        Assert.Equal(0, content.Index);

        Assert.True(content.TryNext());
        Assert.True(content.TryPrevious());
        Assert.Equal(0, content.Index);
    }

    [Fact]
    public void SinglePageContentCannotPage()
    {
        DisplayContent content = Build("约3:16");

        Assert.False(content.TryNext());
        Assert.False(content.TryPrevious());
    }

    // ---------------- 内容正确性 ----------------

    [Fact]
    public void PagesUseCleanedTextByDefault()
    {
        Page page = Assert.Single(Build("创1:1").Pages);

        Assert.Equal("创世记 1:1", page.Label);
        Assert.DoesNotContain('　', page.Body);   // 敬空已剥除
    }

    /// <summary>config.text.useRawText=true 时用 text_raw（P1-4，DB 已就绪）。</summary>
    [Fact]
    public void RawTextModeUsesUncleanedText()
    {
        Page page = Assert.Single(Build("创1:1", useRawText: true).Pages);
        Assert.Contains('　', page.Body);
    }

    [Fact]
    public void CurrentTracksIndex()
    {
        DisplayContent content = Build("诗23:1-6");

        Assert.Equal("诗篇 23:1", content.Current!.Label);
        content.TryNext();
        Assert.Equal("诗篇 23:2", content.Current!.Label);
    }

    // ---------------- 自由文本（P0-4）----------------

    [Fact]
    public void FreeTextIsOnePageVerbatimWithNoLabel()
    {
        const string text = "欢迎新朋友";

        DisplayContent content = ContentBuilder.FromFreeText(text);

        Assert.Equal(ContentKind.FreeText, content.Kind);
        Assert.Null(content.Source);

        Page page = Assert.Single(content.Pages);
        Assert.Equal(text, page.Body);              // 原样，不清洗不截断
        Assert.Equal(string.Empty, page.Label);     // 自由文本没有出处标签
        Assert.Equal(string.Empty, content.PageIndicator);
    }

    /// <summary>
    /// 自由文本里的空格必须原样保留——归一化只用于解析判定，不能污染上屏内容。
    /// </summary>
    [Fact]
    public void FreeTextPreservesWhitespaceExactly()
    {
        const string text = "今晚 7:30 祷告会";
        Assert.Equal(text, Assert.Single(ContentBuilder.FromFreeText(text).Pages).Body);
    }

    [Fact]
    public void EmptyContentIsSafeToQuery()
    {
        var empty = new DisplayContent();

        Assert.True(empty.IsEmpty);
        Assert.Null(empty.Current);
        Assert.False(empty.TryNext());
        Assert.False(empty.TryPrevious());
        Assert.Equal(string.Empty, empty.PageIndicator);
    }
}
