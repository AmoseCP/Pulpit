using System.Collections.Generic;
using System.Globalization;
using Pulpit.Core.Data;

namespace Pulpit.Core.Content;

public enum ContentKind
{
    /// <summary>解析成功的经文引用。</summary>
    Scripture,

    /// <summary>不匹配引用格式的输入，原样上屏（P0-4）。</summary>
    FreeText,
}

/// <summary>副屏上的一页。<c>Label</c> 为空表示无出处标签（自由文本）。</summary>
public sealed record Page(string Label, string Body);

/// <summary>
/// 一次投放的全部内容与当前页位置。
/// </summary>
/// <remarks>
/// L10：超长内容按**并节组**分页，一页一节，字号恒定。分页在 <see cref="ContentBuilder"/>
/// 里就切好了，本类只管翻页游标。
/// </remarks>
public sealed class DisplayContent
{
    public ContentKind Kind { get; init; }

    public IReadOnlyList<Page> Pages { get; init; } = [];

    public int Index { get; set; }

    /// <summary>原始引用，供 F10 换语言时重查（L13：v1 无英文库，但代码路径必须存在）。</summary>
    public VerseRef? Source { get; init; }

    public int PageCount => Pages.Count;

    public bool HasMultiplePages => Pages.Count > 1;

    public bool IsEmpty => Pages.Count == 0;

    /// <summary>当前页；无内容时返回 null。</summary>
    public Page? Current =>
        Index >= 0 && Index < Pages.Count ? Pages[Index] : null;

    /// <summary>页码指示器「2/3」。单页时返回空串——只有多页才显示（M2）。</summary>
    public string PageIndicator => HasMultiplePages
        ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Index + 1, Pages.Count)
        : string.Empty;

    /// <summary>
    /// 前进一页。**已在末页返回 false 且不动**——M4 验收标准明确要求不循环
    /// （末页再按 F8 无动作），循环会让操作员以为翻页失灵而反复按。
    /// </summary>
    public bool TryNext()
    {
        if (Index + 1 >= Pages.Count)
        {
            return false;
        }

        Index++;
        return true;
    }

    /// <summary>后退一页。已在首页返回 false 且不动，同样不循环。</summary>
    public bool TryPrevious()
    {
        if (Index <= 0)
        {
            return false;
        }

        Index--;
        return true;
    }
}
