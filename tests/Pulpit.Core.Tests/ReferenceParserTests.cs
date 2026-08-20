using System.Collections.Generic;
using Pulpit.Core.Data;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// DEVELOPMENT_PLAN §6「解析成功」/「解析报错」/「走自由文本」三张表，逐行落成用例。
/// </summary>
[Collection(BibleCollection.Name)]
public sealed class ReferenceParserTests
{
    private readonly BibleFixture _fx;

    public ReferenceParserTests(BibleFixture fixture) => _fx = fixture;

    // ---------------- 解析成功 ----------------

    [Theory]
    // 输入,            期望出处
    [InlineData("约3:16", "约翰福音 3:16")]              // 基本
    [InlineData("约翰福音3:16", "约翰福音 3:16")]          // 全称
    [InlineData("yh3:16", "约翰福音 3:16")]              // 拼音码
    [InlineData("john3:16", "约翰福音 3:16")]            // 英文全称
    [InlineData("jhn3:16", "约翰福音 3:16")]             // 英文缩写
    [InlineData("约３：１６", "约翰福音 3:16")]             // 全角数字与冒号
    [InlineData("罗 8 : 28", "罗马书 8:28")]             // 空格容错
    [InlineData("约1:1", "约翰福音 1:1")]                // ⚠ 绝不能解析成约翰壹书
    [InlineData("约一3:16", "约翰壹书 3:16")]             // 数字书卷
    [InlineData("约翰一书3:16", "约翰壹书 3:16")]          // 同上全称
    [InlineData("1jn3:16", "约翰壹书 3:16")]             // 英文数字前缀
    [InlineData("门1:6", "腓利门书 1:6")]                // 门/腓 不混
    [InlineData("该2:9", "哈该书 2:9")]                  // 单字简称
    [InlineData("撒上17:45", "撒母耳记上 17:45")]          // 上下卷
    [InlineData("民1:21", "民数记 1:20-21")]             // ⚠ 并节
    [InlineData("诗8:7", "诗篇 8:6-8")]                  // ⚠ 三节并一
    [InlineData("约5:4", "约翰福音 5:3-4")]              // 古卷异文并节
    public void ParsesReferenceAndProducesExpectedLabel(string input, string expectedLabel)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error),
            $"「{input}」应解析成功，实际 error={error ?? "(无)"}");
        Assert.Null(error);
        Assert.NotNull(reference);

        IReadOnlyList<VerseText> verses = _fx.Repository.Lookup(reference);

        Assert.NotEmpty(verses);
        Assert.Equal(expectedLabel, verses[0].Label);

        // 并节组里被合并掉的节号在原库是空串；本库已让组内每个节号都能取到完整文本。
        Assert.False(string.IsNullOrWhiteSpace(verses[0].TextDisplay),
            $"「{input}」出处对了但文本为空——并节解析出问题了");
    }

    /// <summary>
    /// 这一条单独拎出来，因为它是 SCHEMA.md 点名的歧义陷阱：
    /// 别名表刻意没有 <c>约1</c> 这种纯数字形式，就是为了让 <c>约1:1</c> 落在约翰福音。
    /// 别「好心」给别名表补 <c>约1</c>，一补这条就反。
    /// </summary>
    [Fact]
    public void John1Colon1IsGospelOfJohnNotFirstJohn()
    {
        Assert.True(_fx.Parser.TryParse("约1:1", out VerseRef? reference, out _));
        Assert.NotNull(reference);

        Assert.Equal(43, reference.BookId);      // 43 = 约翰福音；62 = 约翰壹书
        Assert.Equal(1, reference.Chapter);
        Assert.Equal(1, reference.Verse);
    }

    // ---------------- 解析报错（应提示，不上屏）----------------

    [Theory]
    [InlineData("约3:99", "约翰福音 3 章只有 36 节")]
    [InlineData("约99:1", "约翰福音只有 21 章")]
    [InlineData("abc3:16", "未知书卷「abc」")]
    public void ReportsFriendlyErrorForOutOfRangeOrUnknownBook(string input, string expectedError)
    {
        Assert.False(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error));
        Assert.Null(reference);
        Assert.Equal(expectedError, error);
    }

    // ---------------- 走自由文本（静默，不报错）----------------

    [Theory]
    [InlineData("欢迎新朋友")]
    [InlineData("今晚 7:30 祷告会")]   // ⚠ 含冒号数字，必须不被误判为引用
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026年感恩节")]
    [InlineData("7:30")]              // 纯时间，没有书卷片段
    public void FallsThroughToFreeTextSilently(string input)
    {
        Assert.False(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error));
        Assert.Null(reference);

        // error 必须是 null。非空 error 会让操作员想投这行字时被拦下（§5 三态语义）。
        Assert.Null(error);
    }

    [Fact]
    public void NullInputFallsThroughToFreeTextSilently()
    {
        Assert.False(_fx.Parser.TryParse(null, out VerseRef? reference, out string? error));
        Assert.Null(reference);
        Assert.Null(error);
    }

    // ---------------- 范围与边界 ----------------

    [Fact]
    public void ParsesVerseRange()
    {
        Assert.True(_fx.Parser.TryParse("诗23:1-3", out VerseRef? reference, out _));
        Assert.NotNull(reference);
        Assert.Equal(1, reference.Verse);
        Assert.Equal(3, reference.EndVerse);
    }

    [Theory]
    [InlineData("诗23:1–3")]   // en dash
    [InlineData("诗23:1—3")]   // em dash
    [InlineData("诗23:1~3")]   // 波浪号
    public void AcceptsAlternativeRangeSeparators(string input)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error),
            $"「{input}」应解析成功，实际 error={error ?? "(无)"}");
        Assert.NotNull(reference);
        Assert.Equal(3, reference.EndVerse);
    }

    [Fact]
    public void DegenerateRangeCollapsesToSingleVerse()
    {
        Assert.True(_fx.Parser.TryParse("诗23:1-1", out VerseRef? reference, out _));
        Assert.NotNull(reference);
        Assert.Null(reference.EndVerse);
    }

    [Fact]
    public void ReversedRangeIsAnError()
    {
        Assert.False(_fx.Parser.TryParse("诗23:3-1", out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RangeEndBeyondChapterIsAnError()
    {
        Assert.False(_fx.Parser.TryParse("约3:16-99", out _, out string? error));
        Assert.Equal("约翰福音 3 章只有 36 节", error);
    }

    /// <summary>章号/节号位数多到溢出 int 时也要给出报错，不能抛异常。</summary>
    [Fact]
    public void AbsurdlyLargeNumbersProduceErrorNotException()
    {
        Assert.False(_fx.Parser.TryParse("约99999999999999:1", out _, out string? error));
        Assert.NotNull(error);
    }

    /// <summary>
    /// 书卷名里可以有数字（1sa / 2co / 约翰1书），正则不能把数字排除在书卷名之外。
    /// </summary>
    [Theory]
    [InlineData("1sa17:45", 9)]
    [InlineData("2co5:17", 47)]
    [InlineData("约翰1书3:16", 62)]
    [InlineData("1john3:16", 62)]
    public void BookNamesMayContainDigits(string input, int expectedBookId)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error),
            $"「{input}」应解析成功，实际 error={error ?? "(无)"}");
        Assert.NotNull(reference);
        Assert.Equal(expectedBookId, reference.BookId);
    }
}
