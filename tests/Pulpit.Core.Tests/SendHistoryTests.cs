using System;
using System.Linq;
using Pulpit.Core.Content;
using Xunit;

namespace Pulpit.Core.Tests;

[Collection(BibleCollection.Name)]
public sealed class SendHistoryTests
{
    private readonly ContentComposer _composer;

    public SendHistoryTests(BibleFixture fixture)
        => _composer = new ContentComposer(fixture.Repository, fixture.Parser);

    /// <summary>把一次真实投放记进历史，返回是否记下了。</summary>
    private bool Send(SendHistory history, string input)
        => history.Record(input, _composer.Compose(input).Content);

    // ---------------- 基本行为 ----------------

    [Fact]
    public void RecordsScriptureMostRecentFirst()
    {
        var history = new SendHistory();

        Assert.True(Send(history, "约3:16"));
        Assert.True(Send(history, "罗8:28"));
        Assert.True(Send(history, "诗23:1-3"));

        Assert.Equal(
            new[] { "诗篇 23:1-3", "罗马书 8:28", "约翰福音 3:16" },
            history.Entries.Select(e => e.Label));
    }

    [Fact]
    public void EntryCarriesInputLabelAndPageCount()
    {
        var history = new SendHistory();
        Send(history, "诗23:1-6");

        HistoryEntry entry = Assert.Single(history.Entries);

        Assert.Equal("诗23:1-6", entry.Input);
        Assert.Equal("诗篇 23:1-6", entry.Label);
        Assert.Equal(6, entry.Pages);
        Assert.Equal("19.23.1.6", entry.ReferenceKey);   // 诗篇 = 19
    }

    [Fact]
    public void MultiReferenceLabelListsEveryReference()
    {
        var history = new SendHistory();
        Send(history, "诗23:1-3;罗8:28");

        Assert.Equal("诗篇 23:1-3 + 罗马书 8:28", Assert.Single(history.Entries).Label);
    }

    /// <summary>并节：标签用真实范围，历史里也一样。</summary>
    [Fact]
    public void MergedGroupUsesRealRangeInLabel()
    {
        var history = new SendHistory();
        Send(history, "民1:21");

        Assert.Equal("民数记 1:20-21", Assert.Single(history.Entries).Label);
    }

    // ---------------- 只记经文 ----------------

    /// <summary>
    /// 计划书 §2 写的是「已投过的**引用**」。自由文本多是一次性通告，
    /// 混进来会把真正想复投的经文淹掉。
    /// </summary>
    [Theory]
    [InlineData("欢迎新朋友")]
    [InlineData("今晚 7:30 祷告会")]
    [InlineData("2026年感恩节")]
    public void FreeTextIsNotRecorded(string input)
    {
        var history = new SendHistory();

        Assert.False(Send(history, input));
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void NullContentIsNotRecorded()
    {
        var history = new SendHistory();

        Assert.False(history.Record("约3:16", null));
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void BlankInputIsNotRecorded()
    {
        var history = new SendHistory();
        DisplayContent content = _composer.Compose("约3:16").Content!;

        Assert.False(history.Record("   ", content));
        Assert.False(history.Record(null, content));
        Assert.Empty(history.Entries);
    }

    // ---------------- 去重按归一化输入 ----------------

    /// <summary>
    /// 去重按**解析出来的引用**，不按输入串——所以这五种写法只占一行。
    /// 只比较输入串是分不出 <c>约3:16</c> 和 <c>约翰福音3:16</c> 的。
    /// </summary>
    [Fact]
    public void SameReferenceWrittenDifferentWaysDeduplicatesToOneEntry()
    {
        var history = new SendHistory();

        Send(history, "约3:16");
        Send(history, "约 3 : 16");
        Send(history, "约３：１６");
        Send(history, "约翰福音3:16");
        Send(history, "john3:16");

        HistoryEntry entry = Assert.Single(history.Entries);

        // 显示更新为最近一次键入的形态。
        Assert.Equal("john3:16", entry.Input);
        Assert.Equal("约翰福音 3:16", entry.Label);
    }

    [Fact]
    public void RepeatMovesEntryToFront()
    {
        var history = new SendHistory();

        Send(history, "约3:16");
        Send(history, "罗8:28");
        Send(history, "诗23:1");
        Assert.Equal(3, history.Count);

        Send(history, "罗 8 : 28");         // 与第二条同一处引用 → 移到最前，不新增

        Assert.Equal(3, history.Count);
        Assert.Equal("罗 8 : 28", history.Entries[0].Input);
        Assert.Equal("罗马书 8:28", history.Entries[0].Label);
    }

    /// <summary>
    /// 连续引用的顺序参与去重：页序不同就是两条。
    /// </summary>
    [Fact]
    public void MultiReferenceOrderIsPartOfTheDedupeKey()
    {
        var history = new SendHistory();

        Send(history, "约3:16;罗8:28");
        Send(history, "罗8:28;约3:16");

        Assert.Equal(2, history.Count);
    }

    /// <summary>范围与单节是不同的引用，各占一行。</summary>
    [Fact]
    public void RangeAndSingleVerseAreDifferentEntries()
    {
        var history = new SendHistory();

        Send(history, "诗23:1");
        Send(history, "诗23:1-3");

        Assert.Equal(2, history.Count);
    }

    // ---------------- 容量 ----------------

    [Fact]
    public void OldestEntriesAreDroppedWhenOverCapacity()
    {
        var history = new SendHistory(capacity: 3);

        Send(history, "约3:16");
        Send(history, "罗8:28");
        Send(history, "诗23:1");
        Send(history, "创1:1");

        Assert.Equal(3, history.Count);
        Assert.Equal(
            new[] { "创世记 1:1", "诗篇 23:1", "罗马书 8:28" },
            history.Entries.Select(e => e.Label));
    }

    [Fact]
    public void DefaultCapacityIsThirty() => Assert.Equal(30, new SendHistory().Capacity);

    [Fact]
    public void CapacityBelowOneIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => { new SendHistory(0); });

    [Fact]
    public void ClearEmptiesTheList()
    {
        var history = new SendHistory();
        Send(history, "约3:16");

        history.Clear();

        Assert.Empty(history.Entries);
        Assert.Equal(0, history.Count);
    }
}
