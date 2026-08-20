using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;
using Xunit;

namespace Pulpit.Core.Tests;

[Collection(BibleCollection.Name)]
public sealed class BibleRepositoryTests
{
    private readonly BibleFixture _fx;

    public BibleRepositoryTests(BibleFixture fixture) => _fx = fixture;

    private IReadOnlyList<VerseText> Lookup(string input)
    {
        Assert.True(_fx.Parser.TryParse(input, out VerseRef? reference, out string? error),
            $"「{input}」应解析成功，实际 error={error ?? "(无)"}");

        return _fx.Repository.Lookup(reference!);
    }

    private string Display(string input) => Lookup(input)[0].TextDisplay;

    // ---------------- 并节去重：一组一页 ----------------

    [Theory]
    [InlineData("诗23:1-3", 3)]
    [InlineData("诗23:1-6", 6)]
    [InlineData("民1:20-21", 1)]   // ⚠ 并节去重，不得出 2 页
    [InlineData("诗8:6-8", 1)]     // ⚠ 三节并一
    [InlineData("约3:16", 1)]
    public void RangeLookupDeduplicatesByMergeHead(string input, int expectedGroups)
        => Assert.Equal(expectedGroups, Lookup(input).Count);

    /// <summary>
    /// 去重靠 SQL 的 <c>GROUP BY merge_head</c>。若哪天有人改成朴素的
    /// <c>WHERE verse BETWEEN</c> 而忘了分组，这条会红：民1:20-21 会返回两条
    /// 一模一样的文本，副屏上就是同一句话连出两页。
    /// </summary>
    [Fact]
    public void MergedGroupYieldsOneRowWithFullRangeLabel()
    {
        IReadOnlyList<VerseText> verses = Lookup("民1:20-21");

        VerseText only = Assert.Single(verses);
        Assert.Equal(20, only.MergeHead);
        Assert.Equal(21, only.MergeLast);
        Assert.Equal("民数记 1:20-21", only.Label);
        Assert.False(string.IsNullOrWhiteSpace(only.TextDisplay));
    }

    /// <summary>并节组内**每个**节号都要能查到完整文本（SCHEMA.md 的核心承诺）。</summary>
    [Theory]
    [InlineData("民1:20")]
    [InlineData("民1:21")]
    [InlineData("诗8:6")]
    [InlineData("诗8:7")]
    [InlineData("诗8:8")]
    public void EveryVerseInsideAMergedGroupResolvesToFullText(string input)
    {
        VerseText verse = Assert.Single(Lookup(input));
        Assert.False(string.IsNullOrWhiteSpace(verse.TextDisplay));
        Assert.True(verse.MergeLast > verse.MergeHead, "这些都是并节，merge_last 应大于 merge_head");
    }

    // ---------------- 文本清洗回归（§6 第四张表）----------------

    [Fact]
    public void Genesis1_1_HasNoHonorificSpaceBeforeShen()
        => Assert.StartsWith("起初，神", Display("创1:1"), StringComparison.Ordinal);

    [Fact]
    public void John3_15_HasNoTranslatorNote()
        => Assert.DoesNotContain("或译", Display("约3:15"), StringComparison.Ordinal);

    [Fact]
    public void Psalm3_2_HasNoSelah()
        => Assert.DoesNotContain("细拉", Display("诗3:2"), StringComparison.Ordinal);

    [Fact]
    public void Matthew18_10_HasNoManuscriptNote()
        => Assert.DoesNotContain("有古卷加", Display("太18:10"), StringComparison.Ordinal);

    [Fact]
    public void Song2_10_HasNoSpeakerMarker()
        => Assert.DoesNotContain("〔新郎〕", Display("歌2:10"), StringComparison.Ordinal);

    [Fact]
    public void Exodus16_35_DoesNotEndWithDanglingParenthesis()
        => Assert.False(Display("出16:35").TrimEnd().EndsWith('（'));

    /// <summary>
    /// ⚠ 反向用例：这个括号是**经文本身的插入语**，不是译注，必须保留。
    /// 清洗规则收紧到把它也剥掉时，这条会红。
    /// </summary>
    [Fact]
    public void Genesis48_14_KeepsInlineScripturalParenthesis()
        => Assert.Contains("（以法莲乃是次子）", Display("创48:14"), StringComparison.Ordinal);

    /// <summary>text_raw 必须保持原貌（含敬空），供 config.text.useRawText 使用。</summary>
    [Fact]
    public void RawTextPreservesHonorificSpace()
        => Assert.Contains("　神", Lookup("创1:1")[0].TextRaw, StringComparison.Ordinal);

    // ---------------- 查询 API ----------------

    [Fact]
    public void ResolveBookUsesNormalizedAlias()
    {
        Assert.Equal(43, _fx.Repository.ResolveBook(TextNormalizer.NormalizeAlias("约")));
        Assert.Equal(43, _fx.Repository.ResolveBook(TextNormalizer.NormalizeAlias("John")));
        Assert.Null(_fx.Repository.ResolveBook("这不是别名"));
        Assert.Null(_fx.Repository.ResolveBook(string.Empty));
    }

    [Fact]
    public void BookInfoAndVerseCountFeedTheFriendlyErrors()
    {
        (int Chapters, string NameZh)? info = _fx.Repository.GetBookInfo(43);
        Assert.NotNull(info);
        Assert.Equal(21, info.Value.Chapters);
        Assert.Equal("约翰福音", info.Value.NameZh);

        Assert.Equal(36, _fx.Repository.GetVerseCount(43, 3));
        Assert.Null(_fx.Repository.GetVerseCount(43, 99));
        Assert.Null(_fx.Repository.GetBookInfo(999));
    }

    [Fact]
    public void LookupOfMissingVerseReturnsEmptyNotNull()
    {
        IReadOnlyList<VerseText> verses = _fx.Repository.Lookup(new VerseRef(43, 3, 999, null));
        Assert.NotNull(verses);
        Assert.Empty(verses);
    }

    [Fact]
    public void SchemaVersionIsReadable() => Assert.Equal("1", _fx.Repository.SchemaVersion);

    // ---------------- 明确异常，不是 NullReferenceException ----------------

    /// <summary>M1 验收标准：数据库缺失时抛出**明确异常**。</summary>
    [Fact]
    public void MissingDatabaseThrowsBibleDatabaseException()
    {
        string missing = Path.Combine(Path.GetTempPath(), "pulpit-不存在的库.db");

        BibleDatabaseException ex = Assert.Throws<BibleDatabaseException>(
            () => { new BibleRepository(missing).Dispose(); });

        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>M1 验收标准：数据库损坏时抛出**明确异常**。</summary>
    [Fact]
    public void CorruptDatabaseThrowsBibleDatabaseException()
    {
        string corrupt = Path.Combine(Path.GetTempPath(),
            $"pulpit-corrupt-{Guid.NewGuid():N}.db");

        File.WriteAllText(corrupt, "这不是 SQLite 文件，只是一段文字。");

        try
        {
            Assert.Throws<BibleDatabaseException>(
                () => { new BibleRepository(corrupt).Dispose(); });
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Fact]
    public void DisposedRepositoryThrowsObjectDisposed()
    {
        var repo = new BibleRepository(_fx.DatabasePath);
        repo.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { repo.ResolveBook("约"); });
    }

    // ---------------- 性能 ----------------

    /// <summary>
    /// M1 验收标准：冷启动首次查询 &lt; 50ms。
    /// </summary>
    /// <remarks>
    /// 「冷」在单个测试进程里做不到严格——xUnit 的执行顺序不保证，本用例之前可能已有
    /// 别的用例把 SQLitePCLRaw 原生库和 JIT 都热过了。所以这条的真正作用是**回归护栏**：
    /// 若有人把持久连接改成每次查询重开连接，或让查询退化成全表扫描，它会红。
    /// 失败时请回报实测毫秒数，不要直接把阈值调大。
    /// </remarks>
    [Fact]
    public void ColdFirstLookupStaysUnder50Ms()
    {
        var sw = Stopwatch.StartNew();

        using var repo = new BibleRepository(_fx.DatabasePath);
        var parser = new ReferenceParser(repo);

        Assert.True(parser.TryParse("约3:16", out VerseRef? reference, out _));
        IReadOnlyList<VerseText> verses = repo.Lookup(reference!);

        sw.Stop();

        Assert.NotEmpty(verses);
        Assert.True(sw.Elapsed.TotalMilliseconds < 50,
            $"冷启动首次查询耗时 {sw.Elapsed.TotalMilliseconds:F1}ms，超过 M1 验收的 50ms");
    }
}
