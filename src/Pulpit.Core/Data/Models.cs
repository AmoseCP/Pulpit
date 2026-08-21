using System;
using System.Collections.Generic;

namespace Pulpit.Core.Data;

/// <summary>一条经文引用。<c>EndVerse</c> 为 null 表示单节。</summary>
public sealed record VerseRef(int BookId, int Chapter, int Verse, int? EndVerse);

/// <summary>
/// 一节（或一个并节组）的经文。
/// </summary>
/// <remarks>
/// 和合本有 81 组经文把多节合并成一段（民 1:20-21、诗 8:6-8 等）。
/// <see cref="MergeHead"/>/<see cref="MergeLast"/> 给出该组的真实范围；
/// 非并节时两者都等于本节节号。
/// </remarks>
public sealed record VerseText(
    int BookId,
    string BookName,
    int Chapter,
    int MergeHead,
    int MergeLast,
    string TextDisplay,
    string TextRaw)
{
    /// <summary>
    /// 出处标签：「约翰福音 3:16」或「民数记 1:20-21」。
    /// <see cref="BookName"/> 随译本语言而变（P1-1）：中文译本给 name_zh，
    /// 英文译本给 name_en——英文经文配英文出处「John 3:16」。
    /// </summary>
    public string Label => MergeLast != MergeHead
        ? $"{BookName} {Chapter}:{MergeHead}-{MergeLast}"
        : $"{BookName} {Chapter}:{MergeHead}";
}

/// <summary>库中一个译本的信息（translations 表的一行）。</summary>
public sealed record TranslationInfo(int Id, string Code, string Name, string Lang);

/// <summary>
/// 英文译本的选取规则（P1-1）。放 Core 是为了可单测——这是「F10 到底投哪个库」的唯一落点。
/// </summary>
public static class TranslationSelector
{
    /// <summary>
    /// 选出 F10 要投的英文译本：优先配置指定的 code（<c>text.englishCode</c>，
    /// 大小写不敏感，且必须真是英文译本——防止手误填成中文库的 code）；
    /// 找不到则回退任一已安装的英文译本（取 id 最大的，后导入的视为更新）；
    /// 库里没有英文译本时返回 null（F10 提示「未安装」）。
    /// </summary>
    public static TranslationInfo? SelectEnglish(
        IReadOnlyList<TranslationInfo> translations, string? preferredCode)
    {
        TranslationInfo? fallback = null;

        foreach (TranslationInfo t in translations)
        {
            if (!string.Equals(t.Lang, "en", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (preferredCode is not null
                && string.Equals(t.Code, preferredCode, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }

            if (fallback is null || t.Id > fallback.Id)
            {
                fallback = t;
            }
        }

        return fallback;
    }
}

/// <summary>
/// 关键词反查索引用的一行——只带搜索与显示需要的字段。
/// </summary>
/// <remarks>
/// 刻意不复用 <see cref="VerseText"/>：那个带 <c>TextRaw</c>，
/// 全库读进来会让内存翻倍，而反查只搜清洗版正文。
/// </remarks>
public sealed record SearchableVerse(
    int BookId,
    string BookNameZh,
    string BookShortZh,
    int Chapter,
    int MergeHead,
    int MergeLast,
    string TextDisplay);
