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

    [Fact]
    public void ListEnglishFiltersOutChineseAndSortsById()
    {
        // 设置界面的译本下拉用这个列表——中文库绝不能混进可切换项。
        IReadOnlyList<TranslationInfo> english =
            TranslationSelector.ListEnglish([Niv2011, Cuv, Niv1984]);

        Assert.Equal([Niv1984, Niv2011], english);
    }

    [Fact]
    public void ListEnglishIsEmptyWhenNoEnglishInstalled()
    {
        Assert.Empty(TranslationSelector.ListEnglish([Cuv]));
        Assert.Empty(TranslationSelector.ListEnglish([]));
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

/// <summary>
/// 中英对照合成（英上中下）。中文是主语言：分页、标签、报错全按中文走，
/// 英文只是每页的补充行——这些性质在这里钉死。
/// </summary>
[Collection(BibleCollection.Name)]
public sealed class BilingualComposeTests
{
    private readonly BibleFixture _fx;
    private readonly ContentComposer _composer;
    private readonly TranslationInfo _english;

    public BilingualComposeTests(BibleFixture fixture)
    {
        _fx = fixture;
        _composer = new ContentComposer(_fx.Repository, _fx.Parser);
        _english = TranslationSelector.SelectEnglish(
            _fx.Repository.GetTranslations(), new Pulpit.Core.Config.TextConfig().EnglishCode)!;
    }

    [Fact]
    public void EnglishOnTopChineseBelowSeparatedByNewline()
    {
        ComposeResult result = _composer.ComposeBilingual("约3:16", useRawText: false, _english.Id);

        Assert.True(result.HasContent);
        Page page = Assert.Single(result.Content!.Pages);

        // 标签与出处按中文走——现场主语言是中文，操作员核对的也是中文出处。
        Assert.Equal("约翰福音 3:16", page.Label);
        Assert.Equal(["约翰福音 3:16"], result.Content.SourceLabels);

        string[] halves = page.Body.Split('\n');
        Assert.Equal(2, halves.Length);
        Assert.StartsWith("For God so loved the world", halves[0]);
        Assert.Contains("神爱世人", halves[1]);
    }

    [Fact]
    public void MergedGroupStaysOnePageWithAllEnglishVersesJoined()
    {
        // 诗 8:6 落在中文并节组 6-8：对照下仍是一页，英文行并入该组全部三节。
        ComposeResult result = _composer.ComposeBilingual("诗8:6", useRawText: false, _english.Id);

        Page page = Assert.Single(result.Content!.Pages);
        Assert.Equal("诗篇 8:6-8", page.Label);

        string english = page.Body.Split('\n')[0];
        IReadOnlyList<VerseText> verses = _fx.Repository.Lookup(new VerseRef(19, 8, 6, 8), _english.Id);
        Assert.Equal(3, verses.Count);
        Assert.All(verses, v => Assert.Contains(v.TextDisplay, english));
    }

    [Fact]
    public void EnglishGapFallsBackToChineseOnlyInsteadOfFailing()
    {
        // NIV 把太 17:21 归入脚注。纯英文投放报错（见上），但对照模式的主语言
        // 是中文——缺一行英文不该把整次投放拦下来，该页只出中文。
        ComposeResult bilingual = _composer.ComposeBilingual("太17:21", useRawText: false, _english.Id);
        ComposeResult chinese = _composer.Compose("太17:21");

        Assert.True(bilingual.HasContent);
        Assert.Equal(
            Assert.Single(chinese.Content!.Pages),
            Assert.Single(bilingual.Content!.Pages));
    }

    [Fact]
    public void ConsecutiveReferencesEachPagePaired()
    {
        ComposeResult result = _composer.ComposeBilingual("约3:16;罗8:28", useRawText: false, _english.Id);

        Assert.True(result.HasContent);
        Assert.Equal(2, result.Content!.PageCount);
        Assert.Equal(2, result.Content.Sources.Count);
        Assert.All(result.Content.Pages, p => Assert.Contains('\n', p.Body));
    }

    [Fact]
    public void ErrorsAndFreeTextFollowTheChinesePath()
    {
        // 三态语义与 Compose 完全一致：报错按中文报，自由文本原样上屏，空输入无事发生。
        Assert.True(_composer.ComposeBilingual("约3:99", useRawText: false, _english.Id).HasError);

        ComposeResult freeText = _composer.ComposeBilingual("欢迎弟兄姊妹", useRawText: false, _english.Id);
        Assert.Equal(ContentKind.FreeText, freeText.Content!.Kind);

        Assert.True(_composer.ComposeBilingual("  ", useRawText: false, _english.Id).IsEmpty);
    }
}
