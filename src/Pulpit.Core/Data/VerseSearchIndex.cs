using System;
using System.Collections.Generic;
using System.Globalization;
using Pulpit.Core.Parsing;

namespace Pulpit.Core.Data;

/// <summary>关键词反查的一条命中。</summary>
/// <param name="Reference">可直接交给 <c>Lookup</c> 的引用。</param>
/// <param name="Label">「约翰福音 3:16」或「民数记 1:20-21」。</param>
/// <param name="InputForm">
/// 填回输入框用的形态，如 <c>约3:16</c>。用 <c>books.short_zh</c> 拼成——
/// 已核对 66 个短称全部能被解析器解析回同一书卷，且无一个以数字结尾。
/// </param>
/// <param name="TextDisplay">清洗版正文，供结果列表预览。</param>
public sealed record SearchHit(VerseRef Reference, string Label, string InputForm, string TextDisplay);

/// <summary>一次反查的结果。</summary>
public sealed record SearchResult
{
    public IReadOnlyList<SearchHit> Hits { get; init; } = [];

    /// <summary>符合条件的总数（可能大于 <see cref="Hits"/> 的条数）。</summary>
    public int TotalMatches { get; init; }

    public bool Truncated => TotalMatches > Hits.Count;

    /// <summary>给操作员看的说明，如「关键词至少 2 个字」。null 表示没什么要说的。</summary>
    public string? Notice { get; init; }
}

/// <summary>
/// 关键词反查（P2-1）：「神爱世人」→ 约翰福音 3:16。
/// </summary>
/// <remarks>
/// <para><b>刻意不用 FTS5，也不碰数据库。</b>计划书 §2 原写「需补 FTS5」，实测两点都不成立：</para>
/// <list type="number">
/// <item><b>FTS5 的 unicode61 分词对中文基本不可用。</b>它把每段连续 CJK 当成**一个 token**，
///   而 FTS5 的短语查询只匹配整 token。原文「神爱世人，甚至将他的独生子赐给他们」里，
///   搜「神爱世人」能中（恰好是一个完整 token），搜「爱世人」「世人」「独生子」
///   甚至「甚至将他」全部 0 条。反查最常用的就是词中间的短语。</item>
/// <item><b>补 FTS5 要往库里写，直接违反 L12（只读打开）</b>与 CLAUDE.md
///   「不要写入、不要迁移」。</item>
/// </list>
/// <para><b>实际做法</b>：把 31021 行清洗版正文一次读进内存（约 2MB，实测建索引共约 52ms），
/// 去掉标点后做子串匹配，每次搜索约 1.5ms。规模摆在这里，索引结构纯属浪费。</para>
/// <para><b>为什么要去标点</b>：操作员记得的是连续的一句话，不会记得逗号落在哪。
/// 「神爱世人甚至」用 <c>LIKE</c> 命中 0 条，去标点后命中 1 条。</para>
/// <para>索引<b>懒建</b>：不搜就不花那 52ms，启动时间不受影响。</para>
/// </remarks>
public sealed class VerseSearchIndex
{
    /// <summary>关键词最短长度（去标点后）。1 个字会命中上千条，没有实用价值。</summary>
    public const int MinimumKeywordLength = 2;

    /// <summary>默认返回上限。</summary>
    public const int DefaultLimit = 50;

    private readonly IBibleRepository _repository;
    private readonly int _transId;

    private List<Entry>? _entries;

    public VerseSearchIndex(IBibleRepository repository, int transId = 1)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _transId = transId;
    }

    /// <summary>索引是否已经建好（用于诊断显示）。</summary>
    public bool IsBuilt => _entries is not null;

    /// <summary>索引里的条数；未建时为 0。</summary>
    public int IndexedVerses => _entries?.Count ?? 0;

    /// <summary>提前建索引。不调也行，首次 <see cref="Search"/> 会自己建。</summary>
    public void Build() => EnsureBuilt();

    public SearchResult Search(string? keyword, int limit = DefaultLimit)
    {
        if (limit < 1)
        {
            limit = 1;
        }

        string needle = TextNormalizer.NormalizeForSearch(keyword);

        if (needle.Length == 0)
        {
            return new SearchResult();
        }

        if (needle.Length < MinimumKeywordLength)
        {
            return new SearchResult
            {
                Notice = $"关键词至少 {MinimumKeywordLength} 个字（标点不算）",
            };
        }

        List<Entry> entries = EnsureBuilt();

        var hits = new List<SearchHit>();
        int total = 0;

        foreach (Entry entry in entries)
        {
            if (!entry.Searchable.Contains(needle, StringComparison.Ordinal))
            {
                continue;
            }

            total++;

            if (hits.Count < limit)
            {
                hits.Add(entry.ToHit());
            }
        }

        return new SearchResult
        {
            Hits = hits,
            TotalMatches = total,
            Notice = total == 0 ? "没有找到" : null,
        };
    }

    private List<Entry> EnsureBuilt()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        IReadOnlyList<SearchableVerse> verses = _repository.LoadSearchableVerses(_transId);

        var built = new List<Entry>(verses.Count);
        foreach (SearchableVerse verse in verses)
        {
            built.Add(new Entry(verse, TextNormalizer.NormalizeForSearch(verse.TextDisplay)));
        }

        _entries = built;
        return _entries;
    }

    /// <summary>索引里的一行：原始数据 + 去标点后的可搜索文本。</summary>
    private sealed record Entry(SearchableVerse Verse, string Searchable)
    {
        internal SearchHit ToHit()
        {
            SearchableVerse v = Verse;

            string label = v.MergeLast != v.MergeHead
                ? $"{v.BookNameZh} {v.Chapter}:{v.MergeHead}-{v.MergeLast}"
                : $"{v.BookNameZh} {v.Chapter}:{v.MergeHead}";

            string inputForm = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}:{2}",
                v.BookShortZh, v.Chapter, v.MergeHead);

            return new SearchHit(
                Reference: new VerseRef(v.BookId, v.Chapter, v.MergeHead, null),
                Label: label,
                InputForm: inputForm,
                TextDisplay: v.TextDisplay);
        }
    }
}
