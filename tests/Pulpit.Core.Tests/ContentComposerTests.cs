using System.Collections.Generic;
using System.Linq;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// <see cref="ContentComposer"/> —— §5 三态语义与 P1-5 连续引用的唯一落点。
/// </summary>
[Collection(BibleCollection.Name)]
public sealed class ContentComposerTests
{
    private readonly BibleFixture _fx;
    private readonly ContentComposer _composer;

    public ContentComposerTests(BibleFixture fixture)
    {
        _fx = fixture;
        _composer = new ContentComposer(_fx.Repository, _fx.Parser);
    }

    // ================= 三态 =================

    [Fact]
    public void ScriptureInputYieldsScriptureContent()
    {
        ComposeResult result = _composer.Compose("约3:16");

        Assert.True(result.HasContent);
        Assert.False(result.HasError);
        Assert.Equal(ContentKind.Scripture, result.Content!.Kind);
        Assert.Equal("约翰福音 3:16", Assert.Single(result.Content.Pages).Label);
    }

    [Fact]
    public void FreeTextInputYieldsFreeTextContentVerbatim()
    {
        ComposeResult result = _composer.Compose("今晚 7:30 祷告会");

        Assert.True(result.HasContent);
        Assert.False(result.HasError);
        Assert.Equal(ContentKind.FreeText, result.Content!.Kind);

        // 原样：空格必须保留（归一化只用于解析判定，不能污染上屏内容）。
        Assert.Equal("今晚 7:30 祷告会", Assert.Single(result.Content.Pages).Body);
    }

    [Fact]
    public void ReferenceShapedErrorYieldsErrorAndNoContent()
    {
        ComposeResult result = _composer.Compose("约3:99");

        Assert.True(result.HasError);
        Assert.False(result.HasContent);
        Assert.Equal("约翰福音 3 章只有 36 节", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("\t\t")]
    public void BlankInputIsNeitherContentNorError(string input)
    {
        ComposeResult result = _composer.Compose(input);

        Assert.True(result.IsEmpty);
        Assert.False(result.HasContent);
        Assert.False(result.HasError);
    }

    [Fact]
    public void NullInputIsNeitherContentNorError()
    {
        ComposeResult result = _composer.Compose(null);

        Assert.True(result.IsEmpty);
        Assert.False(result.HasContent);
        Assert.False(result.HasError);
    }

    // ================= P1-5 连续引用 =================

    [Theory]
    [InlineData("约3:16;罗8:28", 2)]
    [InlineData("约3:16；罗8:28", 2)]        // 全角分号，NFKC 折成半角
    [InlineData("约3:16 ; 罗8:28", 2)]      // 分隔符两侧有空格
    [InlineData("约3:16;", 1)]              // 尾随分隔符
    [InlineData(";约3:16", 1)]              // 前导分隔符
    [InlineData("诗23:1-3;约3:16", 4)]      // 范围 + 单节
    [InlineData("民1:20-21;诗8:6-8", 2)]    // 两个并节组，各 1 页
    [InlineData("约3:16;罗8:28;诗23:1", 3)]
    public void MultipleReferencesConcatenatePagesInInputOrder(string input, int expectedPages)
    {
        ComposeResult result = _composer.Compose(input);

        Assert.True(result.HasContent, result.Error);
        Assert.Equal(ContentKind.Scripture, result.Content!.Kind);
        Assert.Equal(expectedPages, result.Content.PageCount);
    }

    [Fact]
    public void MultipleReferencesKeepPageOrderAndLabels()
    {
        ComposeResult result = _composer.Compose("诗23:1-3;约3:16");

        IReadOnlyList<Page> pages = result.Content!.Pages;

        Assert.Equal(
            new[] { "诗篇 23:1", "诗篇 23:2", "诗篇 23:3", "约翰福音 3:16" },
            pages.Select(p => p.Label));
    }

    [Fact]
    public void MultipleReferencesRecordEverySourceAndLabel()
    {
        ComposeResult result = _composer.Compose("诗23:1-3;民1:21");

        DisplayContent content = result.Content!;

        Assert.Equal(2, content.Sources.Count);

        // 出处标签用真实范围：民1:21 落在并节组 1:20-21。
        Assert.Equal(new[] { "诗篇 23:1-3", "民数记 1:20-21" }, content.SourceLabels);

        // 多处引用时 Source 退化为 null，权威字段是 Sources（§5 契约的兼容成员）。
        Assert.Null(content.Source);
    }

    [Fact]
    public void SingleReferenceStillExposesSource()
    {
        DisplayContent content = _composer.Compose("约3:16").Content!;

        Assert.NotNull(content.Source);
        Assert.Equal(43, content.Source.BookId);
        Assert.Equal(new[] { "约翰福音 3:16" }, content.SourceLabels);
    }

    /// <summary>
    /// 刻意不去重：重复很可能是有意的（同一节前后各念一次），
    /// 而静默吞掉一处引用比多出一页更难察觉。
    /// </summary>
    [Fact]
    public void RepeatedReferenceIsNotDeduplicated()
        => Assert.Equal(2, _composer.Compose("约3:16;约3:16").Content!.PageCount);

    /// <summary>多段里有一段错 → 报错必须**点名到那一段**，否则不知道是哪处写错了。</summary>
    [Fact]
    public void ErrorInOneSegmentNamesThatSegment()
    {
        ComposeResult result = _composer.Compose("约3:16;约3:99");

        Assert.True(result.HasError);
        Assert.Equal("「约3:99」：约翰福音 3 章只有 36 节", result.Error);
    }

    /// <summary>单段时不加段名前缀——多余的引号只会让报错更难读。</summary>
    [Fact]
    public void ErrorInSingleSegmentHasNoSegmentPrefix()
        => Assert.Equal("约翰福音 3 章只有 36 节", _composer.Compose("约3:99").Error);

    /// <summary>
    /// 任何一段不像引用 → **整串**当自由文本原样上屏，不做混合投放。
    /// 混合结果难以预期，而可预期比聪明重要。
    /// </summary>
    [Theory]
    [InlineData("约3:16;欢迎新朋友")]
    [InlineData("欢迎新朋友;约3:16")]
    [InlineData(";;")]
    public void AnyNonReferenceSegmentMakesTheWholeInputFreeText(string input)
    {
        ComposeResult result = _composer.Compose(input);

        Assert.True(result.HasContent);
        Assert.Equal(ContentKind.FreeText, result.Content!.Kind);
        Assert.Equal(input, Assert.Single(result.Content.Pages).Body);
    }

    /// <summary>
    /// ⚠ 分隔符只认 <c>;</c>。中文正文里逗号和顿号满地跑，
    /// 把它们当分隔符会让大量自由文本被误判成引用格式。
    /// </summary>
    [Theory]
    [InlineData("今天下午两点；明天上午十点")]   // 全角分号，但两段都不是引用 → 自由文本
    [InlineData("约3:16,罗8:28")]              // 半角逗号不是分隔符
    [InlineData("约3:16，罗8:28")]              // 全角逗号不是分隔符
    [InlineData("约3:16、罗8:28")]              // 顿号不是分隔符
    public void OnlySemicolonSeparates(string input)
    {
        ComposeResult result = _composer.Compose(input);

        Assert.True(result.HasContent);
        Assert.Equal(ContentKind.FreeText, result.Content!.Kind);
        Assert.Equal(input, Assert.Single(result.Content.Pages).Body);
    }

    // ================= 降级：经文库不可用 =================

    /// <summary>
    /// 经文库打不开时，**一切输入都走自由文本**——包括看起来像引用的。
    /// 这条降级路径最容易悄悄坏掉，所以钉成用例。
    /// </summary>
    [Theory]
    [InlineData("约3:16")]
    [InlineData("约3:99")]
    [InlineData("约3:16;罗8:28")]
    [InlineData("欢迎新朋友")]
    public void WithoutRepositoryEverythingIsFreeText(string input)
    {
        var degraded = new ContentComposer(repository: null, parser: null);

        Assert.False(degraded.ScriptureAvailable);

        ComposeResult result = degraded.Compose(input);

        Assert.True(result.HasContent);
        Assert.False(result.HasError);
        Assert.Equal(ContentKind.FreeText, result.Content!.Kind);
        Assert.Equal(input, Assert.Single(result.Content.Pages).Body);
    }

    [Fact]
    public void WithRepositoryScriptureIsAvailable() => Assert.True(_composer.ScriptureAvailable);

    // ================= P1-4 原文 / 清洗版 =================

    [Fact]
    public void UseRawTextFlagSelectsUncleanedText()
    {
        string clean = _composer.Compose("创1:1", useRawText: false).Content!.Pages[0].Body;
        string raw = _composer.Compose("创1:1", useRawText: true).Content!.Pages[0].Body;

        Assert.DoesNotContain('　', clean);   // 敬空已剥除
        Assert.Contains('　', raw);           // 原貌保留
    }

    [Fact]
    public void UseRawTextAppliesToEverySegmentOfAMultiReference()
    {
        DisplayContent content = _composer.Compose("创1:1;创1:2", useRawText: true).Content!;

        Assert.Equal(2, content.PageCount);
        Assert.All(content.Pages, page => Assert.NotEmpty(page.Body));
        Assert.Contains('　', content.Pages[0].Body);
    }

    // ================= ResolvedReference.Label =================

    [Theory]
    [InlineData("约3:16", "约翰福音 3:16")]
    [InlineData("民1:21", "民数记 1:20-21")]     // 并节：标签用真实范围
    [InlineData("诗8:7", "诗篇 8:6-8")]          // 三节并一
    [InlineData("诗23:1-3", "诗篇 23:1-3")]      // 跨多节：首 merge_head 到末 merge_last
    [InlineData("诗23:1-6", "诗篇 23:1-6")]
    public void ResolvedReferenceLabelUsesTheRealRange(string input, string expected)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out _));

        var resolved = new ResolvedReference(reference!, _fx.Repository.Lookup(reference!));

        Assert.Equal(expected, resolved.Label);
    }

    [Fact]
    public void ResolvedReferenceWithNoVersesHasEmptyLabel()
    {
        var resolved = new ResolvedReference(new VerseRef(43, 3, 16, null), []);

        Assert.Equal(string.Empty, resolved.Label);
    }
}
