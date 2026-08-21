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

    /// <summary>多行歌词（P2-2）：保留换行，按小节分页，无出处标签。</summary>
    Lyrics,
}

/// <summary>
/// 副屏上的一页。<c>Label</c> 为空表示无出处标签（自由文本）。
/// </summary>
/// <remarks>
/// <para><c>SecondaryBody</c>/<c>SecondaryLabel</c> 是中英对照的第二段落（英文），
/// 显示在主段落**上方**、带自己的出处——对照页每种语言的经文与出处各自成组。
/// 非对照内容两者为空串，渲染层据 <c>SecondaryBody</c> 是否为空选布局：
/// 空 → 单段落 + 页脚出处（原有形态）；非空 → 双段落、出处各随其文右对齐。</para>
/// </remarks>
public sealed record Page(
    string Label,
    string Body,
    string SecondaryBody = "",
    string SecondaryLabel = "");

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

    /// <summary>
    /// 本次投放涉及的全部引用，按输入顺序。自由文本时为空。
    /// </summary>
    /// <remarks>
    /// P1-5 连续引用（<c>约3:16;罗8:28</c>）之后一次投放可能有多处引用，
    /// 所以权威字段是这个列表；<see cref="Source"/> 退化为「恰好只有一处」时的便捷访问。
    /// F10 换语言要重查时用本列表。
    /// </remarks>
    public IReadOnlyList<VerseRef> Sources { get; init; } = [];

    /// <summary>
    /// 每一处引用的出处标签，与 <see cref="Sources"/> 一一对应。
    /// 供控制窗口的模式指示显示「约翰福音 3:16 + 罗马书 8:28」。
    /// </summary>
    public IReadOnlyList<string> SourceLabels { get; init; } = [];

    /// <summary>
    /// 恰好只有一处引用时返回它，否则返回 null（自由文本、或多处引用）。
    /// 保留此成员是为了兼容 §5 的契约。
    /// </summary>
    public VerseRef? Source => Sources.Count == 1 ? Sources[0] : null;

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
