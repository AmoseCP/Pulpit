using System;
using System.Globalization;
using System.Text;

namespace Pulpit.Core.Parsing;

/// <summary>
/// 输入归一化。**两个方法的差别是这里最容易出事的地方，别合并。**
/// </summary>
/// <remarks>
/// <see cref="NormalizeInput"/> 作用于整串，只做 NFKC + 去空白，
/// **刻意不去 <c>-</c>**——范围引用 <c>诗23:1-3</c> 一旦被去掉连字符就变成 <c>诗23:13</c>。
/// <para>
/// <see cref="NormalizeAlias"/> 只作用于**已经切出来的书卷片段**，
/// 才执行 SCHEMA.md 规定的完整规则：NFKC → 去 <c>空格 . - _ ·</c> → 转小写。
/// </para>
/// </remarks>
public static class TextNormalizer
{
    /// <summary>别名归一化要剥掉的连接符。</summary>
    private const string AliasStripChars = ".-_·";

    /// <summary>
    /// 整串轻归一化：NFKC（全角转半角，含全角数字与全角冒号）+ 去掉所有空白。
    /// 供引用解析使用；**不可**用于别名查询。
    /// </summary>
    public static string NormalizeInput(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string nfkc = input.Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(nfkc.Length);
        foreach (char c in nfkc)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 关键词反查用的归一化：NFKC → 去掉**全部空白、标点、符号**。
    /// </summary>
    /// <remarks>
    /// <para>为什么要去标点：操作员记得的是连续的一句话，不会记得逗号落在哪。
    /// 原文「神爱世人，甚至将他的独生子…」，操作员输入「神爱世人甚至」——
    /// 不去标点就一条也搜不到。实测这类跨标点短语用 <c>LIKE</c> 命中 0 条，
    /// 去标点后命中 1 条。</para>
    /// <para>用 Unicode 分类而不是手列标点表：中文标点散落在
    /// Po/Ps/Pe/Pi/Pf/Pd 多个分类里（<c>，。、；：？！…—「」『』（）《》〔〕【】“”‘’·</c>），
    /// 手列必然漏。已核对：NFKC 之后这些字符全部落在 标点/符号/空白 三类之内，
    /// 而汉字（Lo）、数字（Nd）、拉丁字母一个都不会被误删。</para>
    /// </remarks>
    public static string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string nfkc = input.Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(nfkc.Length);
        foreach (char c in nfkc)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSeparator(c))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 别名归一化：NFKC → 去 <c>空格 . - _ ·</c> → 转小写。
    /// 与 <c>book_aliases.alias</c> 列的存储形态一致（SCHEMA.md）。
    /// </summary>
    public static string NormalizeAlias(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return string.Empty;
        }

        string nfkc = alias.Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(nfkc.Length);
        foreach (char c in nfkc)
        {
            if (char.IsWhiteSpace(c) || AliasStripChars.Contains(c, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append(c);
        }

        // 别名表存的是小写；书卷名可能是 ASCII（john / 1jn），用 Invariant 避免
        // 土耳其语 I 之类的区域性大小写陷阱。
        return sb.ToString().ToLower(CultureInfo.InvariantCulture);
    }
}
