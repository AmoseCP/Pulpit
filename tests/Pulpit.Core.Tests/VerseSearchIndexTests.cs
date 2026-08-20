using System;
using System.Collections.Generic;
using System.Linq;
using Pulpit.Core.Data;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// 关键词反查（P2-1）。跑在真库上。
/// </summary>
[Collection(BibleCollection.Name)]
public sealed class VerseSearchIndexTests
{
    private readonly BibleFixture _fx;
    private readonly VerseSearchIndex _index;

    public VerseSearchIndexTests(BibleFixture fixture)
    {
        _fx = fixture;
        _index = new VerseSearchIndex(_fx.Repository);
    }

    // ---------------- 基本命中 ----------------

    [Theory]
    [InlineData("神爱世人", "约翰福音 3:16")]
    [InlineData("耶和华是我的牧者", "诗篇 23:1")]
    [InlineData("起初，神创造天地", "创世记 1:1")]
    public void FindsTheVerseByPhrase(string keyword, string expectedLabel)
    {
        SearchResult result = _index.Search(keyword);

        Assert.Contains(expectedLabel, result.Hits.Select(h => h.Label));
    }

    /// <summary>
    /// ⚠ 本功能存在的主要理由：操作员记得的是**连续的一句话**，不会记得逗号落在哪。
    /// 原文是「神爱世人，甚至将他的独生子…」，输入「神爱世人甚至」必须能命中。
    /// SQL 的 LIKE 在这里是 0 条。
    /// </summary>
    [Theory]
    [InlineData("神爱世人甚至")]
    [InlineData("起初神创造天地")]
    [InlineData("神爱世人，甚至")]
    [InlineData("神爱世人 甚至")]
    public void MatchesAcrossPunctuationAndWhitespace(string keyword)
        => Assert.NotEmpty(_index.Search(keyword).Hits);

    [Fact]
    public void MatchesPhraseInTheMiddleOfAVerse()
    {
        // FTS5 + unicode61 在这一条上是 0 命中（整段 CJK 被当成一个 token）。
        Assert.NotEmpty(_index.Search("独生子").Hits);
        Assert.NotEmpty(_index.Search("爱世人").Hits);
    }

    [Fact]
    public void FullWidthInputIsFolded()
    {
        // 全角数字/标点经 NFKC 折半角后再去标点，与半角输入等价。
        Assert.NotEmpty(_index.Search("神爱世人，甚至").Hits);
        Assert.NotEmpty(_index.Search("神爱世人,甚至").Hits);
    }

    // ---------------- 命中内容正确 ----------------

    [Fact]
    public void HitCarriesReferenceLabelInputFormAndText()
    {
        SearchHit hit = Assert.Single(
            _index.Search("神爱世人").Hits.Where(h => h.Label == "约翰福音 3:16"));

        Assert.Equal(43, hit.Reference.BookId);
        Assert.Equal(3, hit.Reference.Chapter);
        Assert.Equal(16, hit.Reference.Verse);
        Assert.Null(hit.Reference.EndVerse);

        Assert.Equal("约3:16", hit.InputForm);
        Assert.StartsWith("神爱世人", hit.TextDisplay, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>InputForm</c> 必须能被解析器解析回同一处经文——否则「点结果复投」这条路是断的。
    /// </summary>
    [Theory]
    [InlineData("神爱世人")]
    [InlineData("耶和华是我的牧者")]
    [InlineData("你们要小心")]
    [InlineData("以法莲乃是次子")]
    public void EveryInputFormRoundTripsThroughTheParser(string keyword)
    {
        IReadOnlyList<SearchHit> hits = _index.Search(keyword).Hits;

        Assert.NotEmpty(hits);

        foreach (SearchHit hit in hits)
        {
            Assert.True(
                _fx.Parser.TryParse(hit.InputForm, out VerseRef? parsed, out string? error),
                $"「{hit.InputForm}」（来自 {hit.Label}）应能解析，实际 error={error ?? "(无)"}");

            Assert.Equal(hit.Reference.BookId, parsed!.BookId);
            Assert.Equal(hit.Reference.Chapter, parsed.Chapter);
            Assert.Equal(hit.Reference.Verse, parsed.Verse);
        }
    }

    /// <summary>
    /// 并节组的标签用真实范围，且**组内只出一行**。
    /// </summary>
    /// <remarks>
    /// 太 18:10-11 是并节组：同一段文本在库里 10、11 两个节号上各存了一份。
    /// 若加载时忘了 <c>GROUP BY merge_head</c>，搜「你们要小心」会出两条一模一样的结果。
    /// </remarks>
    [Fact]
    public void MergedGroupAppearsOnceWithItsRealRange()
    {
        IReadOnlyList<SearchHit> hits = _index.Search("你们要小心").Hits;

        SearchHit merged = Assert.Single(
            hits.Where(h => h.Label == "马太福音 18:10-11"));

        Assert.Equal(18, merged.Reference.Chapter);
        Assert.Equal(10, merged.Reference.Verse);      // 引用指向组首节
        Assert.Equal("太18:10", merged.InputForm);

        // 整个结果集里没有重复标签。
        Assert.Equal(hits.Count, hits.Select(h => h.Label).Distinct().Count());
    }

    [Fact]
    public void ResultsAreInCanonicalOrder()
    {
        IReadOnlyList<SearchHit> hits = _index.Search("耶和华").Hits;

        Assert.NotEmpty(hits);

        for (int i = 1; i < hits.Count; i++)
        {
            VerseRef a = hits[i - 1].Reference;
            VerseRef b = hits[i].Reference;

            bool ordered = a.BookId < b.BookId
                || (a.BookId == b.BookId && a.Chapter < b.Chapter)
                || (a.BookId == b.BookId && a.Chapter == b.Chapter && a.Verse < b.Verse);

            Assert.True(ordered, $"结果未按正典顺序：{hits[i - 1].Label} 出现在 {hits[i].Label} 之前");
        }
    }

    // ---------------- 边界 ----------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("，。、")]      // 全是标点，去掉后为空
    public void BlankOrPunctuationOnlyKeywordYieldsNothingAndNoNotice(string keyword)
    {
        SearchResult result = _index.Search(keyword);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.TotalMatches);
        Assert.Null(result.Notice);
    }

    [Fact]
    public void NullKeywordIsSafe() => Assert.Empty(_index.Search(null).Hits);

    /// <summary>单字会命中上千条，没有实用价值——明确提示而不是刷一屏。</summary>
    [Theory]
    [InlineData("爱")]
    [InlineData("神")]
    public void SingleCharacterKeywordIsRefusedWithANotice(string keyword)
    {
        SearchResult result = _index.Search(keyword);

        Assert.Empty(result.Hits);
        Assert.NotNull(result.Notice);
        Assert.Contains("至少", result.Notice);
    }

    [Fact]
    public void NoMatchCarriesANotice()
    {
        SearchResult result = _index.Search("这句话圣经里没有出现过");

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal("没有找到", result.Notice);
    }

    [Fact]
    public void ResultsAreCappedButTotalIsReported()
    {
        SearchResult result = _index.Search("耶和华", limit: 5);

        Assert.Equal(5, result.Hits.Count);
        Assert.True(result.TotalMatches > 5);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void NotTruncatedWhenEverythingFits()
    {
        SearchResult result = _index.Search("神爱世人", limit: 50);

        Assert.False(result.Truncated);
        Assert.Equal(result.Hits.Count, result.TotalMatches);
    }

    [Fact]
    public void LimitBelowOneIsTreatedAsOne()
        => Assert.Single(_index.Search("耶和华", limit: 0).Hits);

    // ---------------- 索引本身 ----------------

    /// <summary>懒建：不搜就不花那 ~52ms，启动时间不受影响。</summary>
    [Fact]
    public void IndexIsBuiltLazily()
    {
        var fresh = new VerseSearchIndex(_fx.Repository);

        Assert.False(fresh.IsBuilt);
        Assert.Equal(0, fresh.IndexedVerses);

        fresh.Search("神爱世人");

        Assert.True(fresh.IsBuilt);
        Assert.True(fresh.IndexedVerses > 30000);
    }

    [Fact]
    public void ExplicitBuildWorks()
    {
        var fresh = new VerseSearchIndex(_fx.Repository);
        fresh.Build();

        Assert.True(fresh.IsBuilt);
    }

    /// <summary>并节去重后应是 31021 行（原 31103，去掉 82 个被合并的节号）。</summary>
    [Fact]
    public void IndexCoversEveryDeduplicatedVerse()
    {
        _index.Build();
        Assert.Equal(31021, _index.IndexedVerses);
    }

    [Fact]
    public void RepositoryLoaderDeduplicatesMergedGroups()
    {
        IReadOnlyList<SearchableVerse> all = _fx.Repository.LoadSearchableVerses();

        Assert.Equal(31021, all.Count);

        // 每个 (book, chapter, merge_head) 只出现一次。
        Assert.Equal(
            all.Count,
            all.Select(v => (v.BookId, v.Chapter, v.MergeHead)).Distinct().Count());
    }

    [Fact]
    public void SearchableVerseCarriesBothBookNames()
    {
        SearchableVerse john = _fx.Repository.LoadSearchableVerses()
            .First(v => v.BookId == 43 && v.Chapter == 3 && v.MergeHead == 16);

        Assert.Equal("约翰福音", john.BookNameZh);
        Assert.Equal("约", john.BookShortZh);
    }

    [Fact]
    public void NullRepositoryIsRejected()
        => Assert.Throws<ArgumentNullException>(() => { new VerseSearchIndex(null!); });
}
