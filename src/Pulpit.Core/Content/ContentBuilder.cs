using System;
using System.Collections.Generic;
using Pulpit.Core.Data;

namespace Pulpit.Core.Content;

/// <summary>一处已解析并查到文本的引用。</summary>
public sealed record ResolvedReference(VerseRef Reference, IReadOnlyList<VerseText> Verses)
{
    /// <summary>
    /// 整处引用的出处标签：<c>约翰福音 3:16</c> / <c>民数记 1:20-21</c> / <c>诗篇 23:1-3</c>。
    /// </summary>
    /// <remarks>
    /// 跨多节时用**首节的 merge_head 到末节的 merge_last**，而不是原始输入里的节号——
    /// 输入 <c>诗8:6</c> 时真实范围是 6-8（并节），标签必须反映真实范围。
    /// </remarks>
    public string Label
    {
        get
        {
            if (Verses.Count == 0)
            {
                return string.Empty;
            }

            VerseText first = Verses[0];
            VerseText last = Verses[^1];

            return first.MergeHead == last.MergeLast
                ? first.Label
                : $"{first.BookNameZh} {first.Chapter}:{first.MergeHead}-{last.MergeLast}";
        }
    }
}

/// <summary><c>VerseText[]</c> → <c>Page[]</c>。</summary>
public static class ContentBuilder
{
    /// <summary>
    /// 一个并节组一页。传入的列表应当已由
    /// <see cref="IBibleRepository.Lookup"/> 按 merge_head 去重——
    /// 本方法不再去重，去重发生在 SQL 里（<c>GROUP BY v.merge_head</c>）。
    /// </summary>
    /// <param name="source">原始引用，写入 <see cref="DisplayContent.Source"/> 供 F10 重查。</param>
    /// <param name="verses">查询结果，顺序即页序。</param>
    /// <param name="useRawText">
    /// 对应 <c>config.text.useRawText</c>。默认 false 用 <c>text_display</c>（已清洗）；
    /// true 用 <c>text_raw</c>（含敬空与译注，仅调试或特殊需求）。
    /// </param>
    public static DisplayContent FromVerses(
        VerseRef source,
        IReadOnlyList<VerseText> verses,
        bool useRawText = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(verses);

        var pages = new List<Page>(verses.Count);
        foreach (VerseText verse in verses)
        {
            pages.Add(new Page(
                Label: verse.Label,
                Body: useRawText ? verse.TextRaw : verse.TextDisplay));
        }

        var resolved = new ResolvedReference(source, verses);

        return new DisplayContent
        {
            Kind = ContentKind.Scripture,
            Pages = pages,
            Index = 0,
            Sources = [source],
            SourceLabels = [resolved.Label],
        };
    }

    /// <summary>
    /// 多处引用合成一次投放（P1-5）。页序即输入序，每处引用贡献自己的若干页。
    /// </summary>
    /// <remarks>
    /// **刻意不做去重**：操作员写 <c>约3:16;约3:16</c> 就出两页。
    /// 重复很可能是有意的（同一节前后各念一次），而静默吞掉一处引用比多出一页更难察觉。
    /// </remarks>
    public static DisplayContent FromReferences(
        IReadOnlyList<ResolvedReference> resolved,
        bool useRawText = false)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var pages = new List<Page>();
        var sources = new List<VerseRef>(resolved.Count);
        var labels = new List<string>(resolved.Count);

        foreach (ResolvedReference item in resolved)
        {
            sources.Add(item.Reference);
            labels.Add(item.Label);

            foreach (VerseText verse in item.Verses)
            {
                pages.Add(new Page(
                    Label: verse.Label,
                    Body: useRawText ? verse.TextRaw : verse.TextDisplay));
            }
        }

        return new DisplayContent
        {
            Kind = ContentKind.Scripture,
            Pages = pages,
            Index = 0,
            Sources = sources,
            SourceLabels = labels,
        };
    }

    /// <summary>
    /// 自由文本：单页、无出处标签、原样上屏（P0-4）。
    /// 不做任何清洗或截断——操作员看到的预览就是副屏上的东西。
    /// </summary>
    public static DisplayContent FromFreeText(string text)
    {
        return new DisplayContent
        {
            Kind = ContentKind.FreeText,
            Pages = [new Page(string.Empty, text ?? string.Empty)],
            Index = 0,
        };
    }
}
