using System.Diagnostics.CodeAnalysis;
using Pulpit.Core.Data;

namespace Pulpit.Core.Parsing;

public interface IReferenceParser
{
    /// <summary>
    /// 三态语义（**这是本项目最容易做错的判断之一**）：
    /// <list type="bullet">
    /// <item>返回 <c>true</c> —— 解析成功，<paramref name="reference"/> 有效。</item>
    /// <item>返回 <c>false</c> 且 <paramref name="error"/> 为 <c>null</c> ——
    ///   「不是引用格式」，**必须静默走自由文本，不得报错**。
    ///   否则操作员想投「欢迎新朋友」会被拦下。</item>
    /// <item>返回 <c>false</c> 且 <paramref name="error"/> 非空 ——
    ///   「是引用格式但有错」（书卷未知、章节越界），应向操作员报错。
    ///   报错只出现在控制窗口，**绝不上副屏**。</item>
    /// </list>
    /// </summary>
    bool TryParse(string? input, [MaybeNullWhen(false)] out VerseRef reference, out string? error);
}
