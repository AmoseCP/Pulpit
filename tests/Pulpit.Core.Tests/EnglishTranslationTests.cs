using System.Collections.Generic;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// P1-1 英文译本选取规则——「F10 到底投哪个库」的唯一落点。纯逻辑，不碰库。
/// </summary>
public sealed class TranslationSelectorTests
{
    private static readonly TranslationInfo Cuv = new(1, "CUV", "和合本（简体）", "zh");
    private static readonly TranslationInfo Niv1984 = new(2, "NIV1984", "New International Version (1984)", "en");
    private static readonly TranslationInfo Niv2011 = new(3, "NIV2011", "New International Version (2011)", "en");

    [Fact]
    public void PrefersTheConfiguredCode()
    {
        TranslationInfo? selected = TranslationSelector.SelectEnglish(
            [Cuv, Niv1984, Niv2011], "NIV1984");

        Assert.Equal(Niv1984, selected);
    }

    [Fact]
    public void CodeMatchIsCaseInsensitive()
    {
        TranslationInfo? selected = TranslationSelector.SelectEnglish(
            [Cuv, Niv1984], "niv1984");

        Assert.Equal(Niv1984, selected);
    }

    [Fact]
    public void FallsBackToNewestEnglishWhenConfiguredCodeIsAbsent()
    {
        // 默认配置 NIV2011、库里只有 1984 的过渡期就是这条路径。
        TranslationInfo? selected = TranslationSelector.SelectEnglish(
            [Cuv, Niv1984], "NIV2011");

        Assert.Equal(Niv1984, selected);
    }

    [Fact]
    public void FallbackPicksHighestIdAmongEnglish()
    {
        TranslationInfo? selected = TranslationSelector.SelectEnglish(
            [Cuv, Niv1984, Niv2011], "ESV");

        Assert.Equal(Niv2011, selected);
    }

    [Fact]
    public void ConfiguredCodeMustActuallyBeEnglish()
    {
        // 手误把 englishCode 填成中文库的 code，不能让 F10 变成第二个 F9。
        TranslationInfo? selected = TranslationSelector.SelectEnglish(
            [Cuv, Niv1984], "CUV");

        Assert.Equal(Niv1984, selected);
    }

    [Fact]
    public void ReturnsNullWhenNoEnglishInstalled()
    {
        Assert.Null(TranslationSelector.SelectEnglish([Cuv], "NIV2011"));
        Assert.Null(TranslationSelector.SelectEnglish([], null));
    }
}

/// <summary>
/// P1-1 英文经文查询与合成，跑在真库上。
/// </summary>
/// <remarks>
/// 译本 id 不写死：先按默认配置 code（NIV2011）走一遍真实的选取规则，
/// 拿到什么英文译本就测什么——库里换成 2011 版后这些断言原样成立
/// （约 3:16 两版开头一致，太 17:21 两版都归脚注）。
/// </remarks>
[Collection(BibleCollection.Name)]
public sealed class EnglishLookupTests
{
    private readonly BibleFixture _fx;
    private readonly ContentComposer _composer;
    private readonly TranslationInfo? _english;

    public EnglishLookupTests(BibleFixture fixture)
    {
        _fx = fixture;
        _composer = new ContentComposer(_fx.Repository, _fx.Parser);
        _english = TranslationSelector.SelectEnglish(
            _fx.Repository.GetTranslations(), new Pulpit.Core.Config.TextConfig().EnglishCode);
    }

    [Fact]
    public void DatabaseShipsAnEnglishTranslation()
    {
        Assert.NotNull(_english);
        Assert.Equal("en", _english!.Lang);
    }

    [Fact]
    public void EnglishComposeYieldsEnglishTextWithEnglishLabel()
    {
        ComposeResult result = _composer.Compose("约3:16", useRawText: false, transId: _english!.Id);

        Assert.True(result.HasContent);
        Page page = Assert.Single(result.Content!.Pages);
        Assert.Equal("John 3:16", page.Label);
        Assert.Contains("For God so loved the world", page.Body);
    }

    [Fact]
    public void ChineseDefaultIsUntouchedByTheTransIdParameter()
    {
        ComposeResult result = _composer.Compose("约3:16");

        Assert.Equal("约翰福音 3:16", Assert.Single(result.Content!.Pages).Label);
    }

    [Fact]
    public void NivFootnotedVerseReportsErrorInsteadOfSilentEmpty()
    {
        // NIV 把太 17:21 归入脚注，库中无行。章节校验（共用 chapter_info）放行后
        // 查不到文本，必须报错而不是静默无内容——副屏保持原状，错误只在控制窗口。
        ComposeResult result = _composer.Compose("太17:21", useRawText: false, transId: _english!.Id);

        Assert.True(result.HasError);
        Assert.False(result.HasContent);
    }

    [Fact]
    public void EnglishHasNoMergedVerses()
    {
        // 诗 8:6-8 中文是一个并节组（一页），英文逐节独立（三页）。
        IReadOnlyList<VerseText> verses = _fx.Repository.Lookup(
            new VerseRef(19, 8, 6, 8), _english!.Id);

        Assert.Equal(3, verses.Count);
        Assert.All(verses, v => Assert.Equal(v.MergeHead, v.MergeLast));
        Assert.Equal("Psalms", verses[0].BookName);
    }
}
