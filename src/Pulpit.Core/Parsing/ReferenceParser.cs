using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Pulpit.Core.Data;

namespace Pulpit.Core.Parsing;

/// <summary>
/// 经文引用解析。
/// </summary>
/// <remarks>
/// <para><b>正则为什么长这样</b>——书卷名里**可以有数字**（<c>1sa</c> <c>2co</c>
/// <c>1john</c> <c>约翰1书</c>），所以绝不能把数字排除在书卷名之外。
/// 这里用惰性的 <c>(.*?)</c> 吃书卷名，让「紧跟其后必须是 章号 + 冒号」这个结构
/// 自己去决定切点：</para>
/// <list type="bullet">
/// <item><c>约1:1</c> → 书卷 <c>约</c>、1 章 1 节 = **约翰福音 1:1**。
///   别名表刻意没有 <c>约1</c> 这种纯数字形式（SCHEMA.md），所以不会跑到约翰壹书去。</item>
/// <item><c>约翰1书3:16</c> → 惰性匹配在 <c>1</c> 后面找不到冒号，会继续吃到
///   <c>约翰1书</c> 才收手。</item>
/// <item><c>1jn3:16</c> → 开头的 <c>1</c> 后面不是冒号，继续吃到 <c>1jn</c>。</item>
/// </list>
/// <para>用 <c>[0-9]</c> 而不是 <c>\d</c>：.NET 的 <c>\d</c> 连全角数字（Unicode Nd）
/// 一起匹配，虽然此处已 NFKC 过，写死 ASCII 区间少一层依赖。</para>
/// <para>范围分隔符收了半角连字符、en/em dash 和波浪号——NFKC 不会把
/// <c>–</c> <c>—</c> 折叠成 <c>-</c>，得自己列。</para>
/// </remarks>
public sealed class ReferenceParser : IReferenceParser
{
    private static readonly Regex Pattern = new(
        @"^(?<book>.*?)(?<chapter>[0-9]+):(?<verse>[0-9]+)(?:[-–—~](?<end>[0-9]+))?$",
        RegexOptions.CultureInvariant);

    private readonly IBibleRepository _repository;

    public ReferenceParser(IBibleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public bool TryParse(string? input, [MaybeNullWhen(false)] out VerseRef reference, out string? error)
    {
        reference = null;
        error = null;

        string normalized = TextNormalizer.NormalizeInput(input);
        if (normalized.Length == 0)
        {
            return false;   // 空输入 → 自由文本（静默）
        }

        Match match = Pattern.Match(normalized);
        if (!match.Success)
        {
            return false;   // 不是「书卷+章:节」结构 → 自由文本（静默）
        }

        string bookToken = match.Groups["book"].Value;
        if (bookToken.Length == 0)
        {
            // 形如 7:30 —— 没有书卷片段，当时间处理，走自由文本。
            return false;
        }

        int? bookId = _repository.ResolveBook(TextNormalizer.NormalizeAlias(bookToken));
        if (bookId is null)
        {
            // 已经是引用结构了，书卷却认不出来 → 这是错，要报（§5 三态语义）。
            // 回显操作员**原样键入**的片段，不回显归一化后的形态。
            error = $"未知书卷「{bookToken}」";
            return false;
        }

        (int Chapters, string NameZh)? info = _repository.GetBookInfo(bookId.Value);
        if (info is null)
        {
            error = $"书卷 {bookId.Value} 不在库中";
            return false;
        }

        int chapter = ParseInt(match.Groups["chapter"].Value);
        if (chapter < 1 || chapter > info.Value.Chapters)
        {
            error = $"{info.Value.NameZh}只有 {info.Value.Chapters} 章";
            return false;
        }

        int? verseCount = _repository.GetVerseCount(bookId.Value, chapter);
        if (verseCount is null)
        {
            error = $"{info.Value.NameZh} {chapter} 章不在库中";
            return false;
        }

        int verse = ParseInt(match.Groups["verse"].Value);
        if (verse < 1 || verse > verseCount.Value)
        {
            error = $"{info.Value.NameZh} {chapter} 章只有 {verseCount.Value} 节";
            return false;
        }

        int? endVerse = null;
        if (match.Groups["end"].Success)
        {
            int end = ParseInt(match.Groups["end"].Value);

            if (end > verseCount.Value)
            {
                error = $"{info.Value.NameZh} {chapter} 章只有 {verseCount.Value} 节";
                return false;
            }

            if (end < verse)
            {
                error = $"节范围颠倒：{verse}-{end}";
                return false;
            }

            // 诗23:1-1 这种退化范围直接当单节，免得下游多一条分支。
            endVerse = end == verse ? null : end;
        }

        reference = new VerseRef(bookId.Value, chapter, verse, endVerse);
        return true;
    }

    /// <summary>
    /// 正则已保证只有 ASCII 数字，唯一的失败可能是位数多到溢出
    /// （<c>约99999999999:1</c>）。溢出按「大得离谱」处理，交给上面的越界分支报错。
    /// </summary>
    private static int ParseInt(string digits)
        => int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : int.MaxValue;
}
