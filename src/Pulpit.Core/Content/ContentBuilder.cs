using System;
using System.Collections.Generic;
using Pulpit.Core.Data;

namespace Pulpit.Core.Content;

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

        return new DisplayContent
        {
            Kind = ContentKind.Scripture,
            Pages = pages,
            Index = 0,
            Source = source,
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
            Source = null,
        };
    }
}
