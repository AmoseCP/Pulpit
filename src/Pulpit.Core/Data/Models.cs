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
    string BookNameZh,
    int Chapter,
    int MergeHead,
    int MergeLast,
    string TextDisplay,
    string TextRaw)
{
    /// <summary>出处标签：「约翰福音 3:16」或「民数记 1:20-21」。</summary>
    public string Label => MergeLast != MergeHead
        ? $"{BookNameZh} {Chapter}:{MergeHead}-{MergeLast}"
        : $"{BookNameZh} {Chapter}:{MergeHead}";
}
