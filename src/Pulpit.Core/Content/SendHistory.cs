using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Pulpit.Core.Data;

namespace Pulpit.Core.Content;

/// <summary>历史记录里的一条。</summary>
/// <param name="Input">操作员当时键入的原串，**复投时重新走一遍解析与查询**。</param>
/// <param name="Label">显示用的出处，如「约翰福音 3:16」或「诗篇 23:1-3 + 罗马书 8:28」。</param>
/// <param name="Pages">该条投放共几页，供列表上显示「6 页」。</param>
/// <param name="ReferenceKey">
/// 去重键，由**解析出来的引用**拼成（书卷.章.节.末节），与输入的写法无关。
/// </param>
public sealed record HistoryEntry(string Input, string Label, int Pages, string ReferenceKey);

/// <summary>
/// 本次聚会已投过的引用（P1-2），可点击复投。
/// </summary>
/// <remarks>
/// <para><b>只记经文，不记自由文本。</b>计划书 §2 写的是「已投过的**引用**」；
/// 自由文本多是一次性通告（「今晚 7:30 祷告会」），混进来会把真正想复投的经文淹掉。</para>
/// <para><b>只在内存里，刻意不落盘。</b>「本次聚会」就是本次进程。持久化既不必要
/// （下次聚会的经文表不一样），也多一层「上周投了什么」被无意保留的问题。</para>
/// <para><b>存的是输入串而不是查好的内容。</b>复投时重新走一遍
/// <see cref="ContentComposer"/>，于是复投自动遵循当前的原文/清洗版设置（P1-4），
/// 不会投出一份带着旧设置的快照。</para>
/// <para><b>去重按解析出来的引用，不按输入串。</b><c>约3:16</c> 与 <c>约翰福音3:16</c>
/// 是同一处经文，列表里不该占两行——而只比较输入串是分不出来的。</para>
/// </remarks>
public sealed class SendHistory
{
    /// <summary>默认容量。一场聚会用到的经文远少于这个数，够了。</summary>
    public const int DefaultCapacity = 30;

    private readonly List<HistoryEntry> _entries = [];
    private readonly int _capacity;

    public SendHistory(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量至少为 1。");
        }

        _capacity = capacity;
    }

    /// <summary>最近投过的在最前面。</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public int Count => _entries.Count;

    public int Capacity => _capacity;

    /// <summary>
    /// 记一次投放。自由文本、空内容、空输入都被忽略并返回 false。
    /// 已存在的引用会移到最前，并把显示更新为最近一次键入的形态。
    /// </summary>
    public bool Record(string? input, DisplayContent? content)
    {
        if (content is null || content.Kind != ContentKind.Scripture || content.IsEmpty)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string key = BuildReferenceKey(content);
        if (key.Length == 0)
        {
            return false;
        }

        string label = content.SourceLabels.Count > 0
            ? string.Join(" + ", content.SourceLabels)
            : content.Pages[0].Label;

        int existing = _entries.FindIndex(
            e => string.Equals(e.ReferenceKey, key, StringComparison.Ordinal));

        if (existing >= 0)
        {
            _entries.RemoveAt(existing);
        }

        _entries.Insert(0, new HistoryEntry(input, label, content.PageCount, key));

        // 超出容量就丢最旧的。
        while (_entries.Count > _capacity)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        return true;
    }

    /// <summary>
    /// 由引用本身拼出去重键。多处引用时按输入顺序拼接——
    /// <c>约3:16;罗8:28</c> 与 <c>罗8:28;约3:16</c> 是两条，因为页序不同。
    /// </summary>
    private static string BuildReferenceKey(DisplayContent content)
    {
        var sb = new StringBuilder();

        foreach (VerseRef reference in content.Sources)
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }

            sb.Append(reference.BookId.ToString(CultureInfo.InvariantCulture))
              .Append('.')
              .Append(reference.Chapter.ToString(CultureInfo.InvariantCulture))
              .Append('.')
              .Append(reference.Verse.ToString(CultureInfo.InvariantCulture))
              .Append('.')
              .Append(reference.EndVerse?.ToString(CultureInfo.InvariantCulture) ?? "-");
        }

        return sb.ToString();
    }

    public void Clear() => _entries.Clear();
}
