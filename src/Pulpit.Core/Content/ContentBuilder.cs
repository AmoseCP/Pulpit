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
                : $"{first.BookName} {first.Chapter}:{first.MergeHead}-{last.MergeLast}";
        }
    }
}

/// <summary>
/// 一处引用的中英对照材料：中文（主语言）的解析结果 + 同一引用查出的英文经文。
/// 英文列表允许为空（该引用范围在英文库中全是空档），对应页退化为只出中文。
/// </summary>
public sealed record BilingualReference(
    ResolvedReference Primary, IReadOnlyList<VerseText> English);

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
    /// 中英对照（英上中下）：分页、出处标签、页序全部沿用中文——一个并节组一页，
    /// 每页正文是「该组的英文（可能多节，空格连接）+ 换行 + 该组的中文」。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么按中文分页而不是英文</b>：中文是现场的主语言，并节组是它的
    /// 自然页界；英文逐节独立（见 EnglishHasNoMergedVerses 测试），照英文分页会把
    /// 诗 8:6-8 这类中文一页的内容切成三页，翻页节奏跟着英文走就本末倒置了。</para>
    /// <para><b>英文空档不报错</b>：NIV 把个别节归入脚注（如太 17:21），
    /// 对照模式下该页只出中文即可——为一节英文把整次投放拦下来，比缺一行英文更糟。
    /// 纯英文投放（F10）仍按 <see cref="Pulpit.Core.Content.ContentComposer"/> 的规则报错。</para>
    /// </remarks>
    public static DisplayContent FromBilingualReferences(
        IReadOnlyList<BilingualReference> resolved,
        bool useRawText = false)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var pages = new List<Page>();
        var sources = new List<VerseRef>(resolved.Count);
        var labels = new List<string>(resolved.Count);

        foreach (BilingualReference pair in resolved)
        {
            sources.Add(pair.Primary.Reference);
            labels.Add(pair.Primary.Label);

            foreach (VerseText verse in pair.Primary.Verses)
            {
                string chinese = useRawText ? verse.TextRaw : verse.TextDisplay;
                string english = JoinEnglish(pair.English, verse.MergeHead, verse.MergeLast, useRawText);

                pages.Add(new Page(
                    Label: verse.Label,
                    Body: english.Length == 0 ? chinese : english + "\n" + chinese));
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
    /// 取出落在中文并节组 [<paramref name="mergeHead"/>, <paramref name="mergeLast"/>]
    /// 范围内的英文经文，按节序空格连接。范围判交而不是判等：万一英文库也有并节，
    /// 只要与该组有交集就归入这一页。
    /// </summary>
    private static string JoinEnglish(
        IReadOnlyList<VerseText> english, int mergeHead, int mergeLast, bool useRawText)
    {
        var parts = new List<string>();

        foreach (VerseText verse in english)
        {
            if (verse.MergeHead <= mergeLast && verse.MergeLast >= mergeHead)
            {
                parts.Add(useRawText ? verse.TextRaw : verse.TextDisplay);
            }
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// 多行歌词（P2-2）：保留换行，**空行即分页点**，无出处标签。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么空行是分页点而不是固定几行一页</b>：歌词天然分小节，
    /// 而小节长短不一。作词人写下来的空行就是「这里该翻页」，比任何固定行数都准。
    /// <paramref name="maxLinesPerPage"/> 只用来把超长小节切开——一屏塞十行谁也看不清。</para>
    /// <para>行内不做修剪以外的任何处理：缩进是排版的一部分，不该被吞掉。
    /// 只去掉行尾空白（那是编辑器留下的噪音）。</para>
    /// </remarks>
    public static DisplayContent FromLyrics(string? text, int maxLinesPerPage = 4)
    {
        if (maxLinesPerPage < 1)
        {
            maxLinesPerPage = 1;
        }

        var pages = new List<Page>();
        var current = new List<string>();

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            pages.Add(new Page(string.Empty, string.Join('\n', current)));
            current.Clear();
        }

        // 先把换行统一成 \n 再切。直接对 ['\r','\n'] 做 Split 的话，
        // Windows 的 \r\n 会切出一个空串，而空串在下面被当成「空行 = 分页点」——
        // 结果每一行都单独成页。
        string normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] lines = normalized.Split('\n');

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();

            if (line.Length == 0)
            {
                // 空行 = 小节边界 = 分页点。连续多个空行只算一次。
                Flush();
                continue;
            }

            current.Add(line);

            if (current.Count >= maxLinesPerPage)
            {
                Flush();
            }
        }

        Flush();

        return new DisplayContent
        {
            Kind = ContentKind.Lyrics,
            Pages = pages,
            Index = 0,
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
