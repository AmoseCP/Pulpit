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
        var resolved = new List<ResolvedReference>(segments.Length);

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

        return ComposeResult.Ok(ContentBuilder.FromReferences(resolved, useRawText));
    }

    private static string Decorate(string segment, string error, bool multiple)
        => multiple ? $"「{segment}」：{error}" : error;
}
