using System;
using System.Collections.Generic;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;

namespace Pulpit.Core.Content;

/// <summary>一次合成的结果。三种互斥状态，对应 §5 的三态语义。</summary>
public sealed record ComposeResult
{
    private static readonly ComposeResult NothingToShow = new();

    /// <summary>可投放的内容。<see cref="Error"/> 非空时必为 null。</summary>
    public DisplayContent? Content { get; init; }

    /// <summary>该向操作员报的错。**只出现在控制窗口，绝不上副屏**（P0-10）。</summary>
    public string? Error { get; init; }

    public bool HasContent => Content is not null;

    public bool HasError => Error is not null;

    /// <summary>没有可投的东西（空输入）。既不投也不报错。</summary>
    public bool IsEmpty => Content is null && Error is null;

    internal static ComposeResult Ok(DisplayContent content) => new() { Content = content };

    internal static ComposeResult Failed(string error) => new() { Error = error };

    internal static ComposeResult Nothing() => NothingToShow;
}

/// <summary>
/// 把操作员敲进输入框的一串字变成待投内容。§5 三态语义的唯一落点。
/// </summary>
/// <remarks>
/// <para>这段逻辑原先长在控制窗口的 code-behind 里，因而无法单测。挪到 Core 之后
/// 「什么时候报错、什么时候静默走自由文本」这个最容易出错的判断有了回归护栏——
/// 包括**经文库不可用时一切都走自由文本**这条降级路径，那种路径最容易悄悄坏掉。</para>
/// <para><b>P1-5 连续引用</b>：分隔符只认 <c>;</c>（NFKC 会把全角 <c>；</c> 折过来）。
/// **刻意不认 <c>,</c>** —— 中文正文里逗号满地跑，把它当分隔符会让大量自由文本被误判。
/// 同理 <c>、</c> 也不认（NFKC 不折叠它）。</para>
/// </remarks>
public sealed class ContentComposer
{
    /// <summary>连续引用的分隔符。</summary>
    public const char ReferenceSeparator = ';';

    private readonly IBibleRepository? _repository;
    private readonly IReferenceParser? _parser;

    /// <summary>
    /// 两个参数都允许为 null——经文库打不开时程序仍要能起来并投自由文本。
    /// 这条降级路径由本类统一承担，调用方不必各自判空。
    /// </summary>
    public ContentComposer(IBibleRepository? repository, IReferenceParser? parser)
    {
        _repository = repository;
        _parser = parser;
    }

    /// <summary>经文查询是否可用。控制窗口据此显示告警。</summary>
    public bool ScriptureAvailable => _repository is not null && _parser is not null;

    /// <param name="input">操作员敲进输入框的原始字符串。</param>
    /// <param name="useRawText">对应 <c>config.text.useRawText</c>。</param>
    /// <param name="transId">
    /// 要查的译本（P1-1）。默认 1（中文 CUV）；F10 英文投放传英文译本的 id。
    /// 解析（书卷别名、章节校验）与译本无关，永远走共用的表。
    /// </param>
    public ComposeResult Compose(string? input, bool useRawText = false, int transId = 1)
    {
        ComposeResult? early = ResolveReferences(input, transId, out List<ResolvedReference> resolved);

        return early ?? ComposeResult.Ok(ContentBuilder.FromReferences(resolved, useRawText));
    }

    /// <summary>
    /// 中英对照投放（英上中下）。中文（trans_id=1）是主语言：解析、报错、出处标签、
    /// 分页全按中文走；英文只是每页的补充行，英文有空档的节该页退化为只出中文
    /// （NIV 把个别节归入脚注是常态，为它报错会把整次投放拦下来，得不偿失）。
    /// </summary>
    /// <param name="englishTransId">英文译本的 trans_id（来自 <c>TranslationSelector.SelectEnglish</c>）。</param>
    public ComposeResult ComposeBilingual(string? input, bool useRawText, int englishTransId)
    {
        ComposeResult? early = ResolveReferences(input, transId: 1, out List<ResolvedReference> resolved);

        if (early is not null)
        {
            return early;
        }

        // early 为 null 意味着解析与查询都走通了，_repository 必非 null。
        var pairs = new List<BilingualReference>(resolved.Count);

        foreach (ResolvedReference item in resolved)
        {
            // 英文按中文结果的**真实范围**查（首节 merge_head 到末节 merge_last），
            // 不能按原始输入：输入「诗8:6」时中文并节组的真实范围是 6-8，
            // 按输入查英文只会拿到第 6 节，7-8 两节就丢了（与出处标签同一个道理）。
            VerseText first = item.Verses[0];
            VerseText last = item.Verses[^1];

            var range = new VerseRef(
                item.Reference.BookId,
                first.Chapter,
                first.MergeHead,
                last.MergeLast == first.MergeHead ? null : last.MergeLast);

            pairs.Add(new BilingualReference(item, _repository!.Lookup(range, englishTransId)));
        }

        return ComposeResult.Ok(ContentBuilder.FromBilingualReferences(pairs, useRawText));
    }

    /// <summary>
    /// 三态判定与逐段解析查询，中文/英文/对照三条投放路径共用。
    /// 返回 null 表示每一段都解析成功且查到文本（结果在 <paramref name="resolved"/> 里）；
    /// 非 null 则是该直接返回给调用方的早退结果（空输入 / 自由文本 / 报错）。
    /// </summary>
    private ComposeResult? ResolveReferences(
        string? input, int transId, out List<ResolvedReference> resolved)
    {
        resolved = [];

        string normalized = TextNormalizer.NormalizeInput(input);

        if (normalized.Length == 0)
        {
            return ComposeResult.Nothing();
        }

        // 此处 input 必非 null：normalized 非空说明原串里有非空白字符。
        string original = input!;

        if (_repository is null || _parser is null)
        {
            return ComposeResult.Ok(ContentBuilder.FromFreeText(original));
        }

        string[] segments = normalized.Split(
            ReferenceSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            // 整串只有分隔符（比如「;;」）。原样上屏。
            return ComposeResult.Ok(ContentBuilder.FromFreeText(original));
        }

        bool multiple = segments.Length > 1;

        foreach (string segment in segments)
        {
            if (!_parser.TryParse(segment, out VerseRef? reference, out string? error))
            {
                if (error is not null)
                {
                    // 是引用格式但有错 → 报错。多段时点名到具体那一段，
                    // 否则操作员对着「约翰福音 3 章只有 36 节」不知道是哪一处写错了。
                    return ComposeResult.Failed(Decorate(segment, error, multiple));
                }

                // 有任何一段不像引用 → **整串**当自由文本原样上屏。
                // 不做「部分是经文部分是文本」的混合投放：那种结果难以预期，
                // 而可预期比聪明重要。
                return ComposeResult.Ok(ContentBuilder.FromFreeText(original));
            }

            IReadOnlyList<VerseText> verses = _repository.Lookup(reference, transId);

            if (verses.Count == 0)
            {
                // 中文（trans_id=1）时解析通过、章节也在范围内却查不到文本 = 库出了问题；
                // 英文译本则确实存在合法的空档（NIV 把 16 节归入脚注，如太 17:21），
                // 两种情况都不该静默上屏，报错措辞由调用方按语境补充。
                return ComposeResult.Failed(Decorate(segment, "该节在库中查不到文本", multiple));
            }

            resolved.Add(new ResolvedReference(reference, verses));
        }

        return null;
    }

    private static string Decorate(string segment, string error, bool multiple)
        => multiple ? $"「{segment}」：{error}" : error;
}
