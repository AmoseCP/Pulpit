using System.Collections.Generic;

namespace Pulpit.Core.Data;

public interface IBibleRepository
{
    /// <summary>
    /// 用**已归一化**的别名解析书卷 ID。归一化规则见
    /// <see cref="Pulpit.Core.Parsing.TextNormalizer.NormalizeAlias"/>。
    /// </summary>
    int? ResolveBook(string normalizedAlias);

    /// <summary>书卷总章数与中文名，用于章节越界的友好报错。</summary>
    (int Chapters, string NameZh)? GetBookInfo(int bookId);

    /// <summary>某章的节数，用于节号越界的友好报错。</summary>
    int? GetVerseCount(int bookId, int chapter);

    /// <summary>
    /// 查询引用对应的经文。范围查询**已按 merge_head 去重**——
    /// 否则 民1:20-21 会返回两条一模一样的文本，被分成两页。
    /// </summary>
    IReadOnlyList<VerseText> Lookup(VerseRef reference, int transId = 1);
}
