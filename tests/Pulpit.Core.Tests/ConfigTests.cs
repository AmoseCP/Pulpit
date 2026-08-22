using System;
using System.Collections.Generic;
using System.Linq;
using Pulpit.Core.Config;
using Xunit;

namespace Pulpit.Core.Tests;

public sealed class ConfigTests
{
    // ---------------- 默认值就是 §7 里的那份 ----------------

    [Fact]
    public void DefaultsMatchThePlannedConfigFile()
    {
        var config = new AppConfig();

        Assert.Null(config.TargetScreenDeviceName);
        Assert.Equal(0.30, config.Band.HeightPercent);
        Assert.Equal("bottom", config.Band.VerticalAnchor);
        Assert.Equal(0.72, config.Band.BackgroundOpacity);
        Assert.Equal("#000000", config.Band.Background);
        Assert.Equal(0.06, config.Band.PaddingPercent);
        Assert.Equal("Microsoft YaHei UI", config.Typography.FontFamily);
        Assert.Equal("SemiBold", config.Typography.FontWeight);
        Assert.Equal(96, config.Typography.MaxFontSize);
        Assert.Equal(0.40, config.Typography.LabelScale);
        Assert.Equal("#FFFFFFFF", config.Typography.Foreground);
        Assert.Equal(250, config.Animation.FadeMs);
        Assert.False(config.Text.UseRawText);
        Assert.Equal("NIV2011", config.Text.EnglishCode);
        Assert.False(config.Text.Bilingual);

        Assert.Equal("F9", config.Hotkeys.SendZh);
        Assert.Equal("F10", config.Hotkeys.SendEn);
        Assert.Equal("F7", config.Hotkeys.PrevPage);
        Assert.Equal("F8", config.Hotkeys.NextPage);
        Assert.Equal("F12", config.Hotkeys.Clear);
    }

    [Fact]
    public void SanitizingDefaultsChangesNothing()
    {
        AppConfig sanitized = new AppConfig().Sanitize(out IReadOnlyList<string> corrections);

        Assert.Empty(corrections);
        Assert.Equal(new AppConfig(), sanitized);
    }

    [Fact]
    public void EmptyBandBackgroundFallsBackToBlack()
    {
        var config = new AppConfig { Band = new BandConfig { Background = " " } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal("#000000", sanitized.Band.Background);
        Assert.Contains(corrections, note => note.Contains("band.background"));
    }

    [Fact]
    public void EmptyEnglishCodeFallsBackToDefault()
    {
        var config = new AppConfig { Text = new TextConfig { EnglishCode = "  " } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal("NIV2011", sanitized.Text.EnglishCode);
        Assert.Contains(corrections, note => note.Contains("englishCode"));
    }

    // ---------------- 非法字段夹回默认，不抛异常（§7）----------------

    [Fact]
    public void OutOfRangeBandValuesAreClampedAndReported()
    {
        var config = new AppConfig
        {
            Band = new BandConfig
            {
                HeightPercent = 5.0,          // 远超 1.0
                BackgroundOpacity = -1,
                PaddingPercent = 9,
                VerticalAnchor = "middle",    // 只允许 bottom / top / center
            },
        };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(1.0, sanitized.Band.HeightPercent);
        Assert.Equal(0.0, sanitized.Band.BackgroundOpacity);
        Assert.Equal(0.30, sanitized.Band.PaddingPercent);
        Assert.Equal("bottom", sanitized.Band.VerticalAnchor);
        Assert.Equal(4, corrections.Count);
    }

    /// <summary>
    /// P1-3 扩展：垂直位置的「居中」「全屏」是合法值，大小写不敏感、不产生修正。
    /// fullscreen 是 L3 的 2026-08-20 修订新增的可选档（默认仍是带状）。
    /// </summary>
    [Theory]
    [InlineData("Center", "center")]
    [InlineData("FullScreen", "fullscreen")]
    public void CenterAndFullscreenAnchorsAreLegal(string written, string expected)
    {
        var config = new AppConfig { Band = new BandConfig { VerticalAnchor = written } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(expected, sanitized.Band.VerticalAnchor);
        Assert.Empty(corrections);
    }

    [Fact]
    public void NaNIsTreatedAsInvalidNotClamped()
    {
        var config = new AppConfig
        {
            Band = new BandConfig { HeightPercent = double.NaN },
        };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(0.30, sanitized.Band.HeightPercent);
        Assert.Single(corrections);
    }

    [Fact]
    public void MinFontSizeAboveMaxIsCorrected()
    {
        var config = new AppConfig
        {
            Typography = new TypographyConfig { MaxFontSize = 40, MinFontSize = 90 },
        };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(40, sanitized.Typography.MaxFontSize);
        Assert.True(sanitized.Typography.MinFontSize <= sanitized.Typography.MaxFontSize);
        Assert.NotEmpty(corrections);
    }

    [Fact]
    public void FadeMsZeroIsLegalBecauseItMeansNoAnimation()
    {
        var config = new AppConfig { Animation = new AnimationConfig { FadeMs = 0 } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(0, sanitized.Animation.FadeMs);
        Assert.Empty(corrections);
    }

    [Fact]
    public void NegativeFadeMsFallsBackToDefault()
    {
        var config = new AppConfig { Animation = new AnimationConfig { FadeMs = -5 } };

        Assert.Equal(250, config.Sanitize(out _).Animation.FadeMs);
    }

    // ================= L7：热键白名单 =================
    // 这一组是本文件存在的**主要理由**。RegisterHotKey 是全局独占——注册了哪个键，
    // 那个键就不再传给 WPS。误注册方向键 = 操作员再也翻不了 PPT，是最严重的回归。

    [Theory]
    [InlineData("F7")]
    [InlineData("F8")]
    [InlineData("F9")]
    [InlineData("F10")]
    [InlineData("F11")]     // 2026-08-21 L7 修订：清屏降级键（F12 被系统占用的机器）
    [InlineData("F12")]
    [InlineData("f9")]      // 大小写不敏感
    [InlineData(" F9 ")]    // 两端空白
    public void WhitelistAcceptsOnlyTheSixFunctionKeys(string key)
        => Assert.True(HotkeyWhitelist.IsAllowed(key));

    /// <summary>
    /// 这些全是 PPT 的翻页 / 黑屏 / 白屏 / 放映键，一个都不许注册。
    /// </summary>
    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("PageUp")]
    [InlineData("PageDown")]
    [InlineData("PgUp")]
    [InlineData("PgDn")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Return")]
    [InlineData("Escape")]
    [InlineData("Esc")]
    [InlineData("B")]
    [InlineData("W")]
    [InlineData("F5")]
    [InlineData("F1")]
    [InlineData("F6")]
    [InlineData("Ctrl+F9")]   // 组合键也不支持——v1 只认裸功能键
    [InlineData("")]
    [InlineData("   ")]
    public void WhitelistRejectsEverythingThatBelongsToThePresentation(string key)
        => Assert.False(HotkeyWhitelist.IsAllowed(key));

    [Fact]
    public void WhitelistRejectsNull() => Assert.False(HotkeyWhitelist.IsAllowed(null));

    /// <summary>
    /// 配置文件不是可信输入：把方向键写进 config.json，Sanitize 必须**拒绝并退回默认**，
    /// 而不是照着注册。这条红了就意味着现场可能出现「操作员翻不了 PPT」。
    /// </summary>
    [Fact]
    public void ArrowKeyInConfigIsRejectedAndFallsBackToDefault()
    {
        var config = new AppConfig
        {
            Hotkeys = new HotkeyConfig
            {
                NextPage = "Right",
                PrevPage = "Left",
                SendZh = "Enter",     // L8：送出键绝不是 Enter
                Clear = "Escape",
            },
        };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal("F8", sanitized.Hotkeys.NextPage);
        Assert.Equal("F7", sanitized.Hotkeys.PrevPage);
        Assert.Equal("F9", sanitized.Hotkeys.SendZh);
        Assert.Equal("F12", sanitized.Hotkeys.Clear);
        Assert.Equal(4, corrections.Count);

        Assert.All(corrections, note => Assert.Contains("不在允许的键位内", note));
    }

    [Fact]
    public void WhitelistCanonicalizesCasingAndWhitespace()
    {
        AppConfig sanitized = new AppConfig
        {
            Hotkeys = new HotkeyConfig { SendZh = " f9 " },
        }.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal("F9", sanitized.Hotkeys.SendZh);
        Assert.Empty(corrections);
    }

    [Fact]
    public void WhitelistExposesExactlySixKeys()
    {
        // F11 是 2026-08-21 的 L7 修订（清屏降级）。再想加键位，先读 DEVELOPMENT_PLAN §1。
        Assert.Equal(6, HotkeyWhitelist.All.Count);
        Assert.Equal("F7 F8 F9 F10 F11 F12", HotkeyWhitelist.AllowedList);
    }

    // ================= 副屏角标（P2-4）=================

    [Fact]
    public void BadgeDefaultsAreOffAndTopRight()
    {
        BadgeConfig badge = new AppConfig().Badge;

        Assert.False(badge.Enabled);          // 不是每场聚会都要挂角标
        Assert.Equal(string.Empty, badge.Text);
        Assert.Equal("topRight", badge.Corner);
        Assert.Equal(0.28, badge.WidthPercent);
        Assert.Equal(0.07, badge.HeightPercent);
        Assert.Equal(0.02, badge.MarginPercent);
        Assert.Equal(0.55, badge.BackgroundOpacity);
    }

    [Theory]
    [InlineData("topRight")]
    [InlineData("topLeft")]
    [InlineData("bottomRight")]
    [InlineData("bottomLeft")]
    [InlineData("TOPLEFT")]      // 大小写不敏感
    [InlineData("bottomright")]
    public void KnownCornersAreAcceptedAndCanonicalized(string corner)
    {
        var config = new AppConfig { Badge = new BadgeConfig { Corner = corner } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Empty(corrections);
        Assert.Contains(sanitized.Badge.Corner, BadgeConfig.Corners);

        // 规范化成 Corners 里的那个写法，而不是原样保留大小写。
        Assert.Equal(
            BadgeConfig.Corners.Single(c => string.Equals(c, corner, StringComparison.OrdinalIgnoreCase)),
            sanitized.Badge.Corner);
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("center")]
    [InlineData("")]
    [InlineData("top")]
    public void UnknownCornerFallsBackToTopRight(string corner)
    {
        var config = new AppConfig { Badge = new BadgeConfig { Corner = corner } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal("topRight", sanitized.Badge.Corner);
        Assert.Single(corrections);
    }

    [Fact]
    public void OutOfRangeBadgeGeometryIsClamped()
    {
        var config = new AppConfig
        {
            Badge = new BadgeConfig
            {
                WidthPercent = 3,
                HeightPercent = 0.9,
                MarginPercent = -1,
                BackgroundOpacity = 5,
            },
        };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(1.00, sanitized.Badge.WidthPercent);
        Assert.Equal(0.30, sanitized.Badge.HeightPercent);
        Assert.Equal(0.0, sanitized.Badge.MarginPercent);
        Assert.Equal(1.0, sanitized.Badge.BackgroundOpacity);
        Assert.Equal(4, corrections.Count);
    }

    [Fact]
    public void BadgeTextIsNotSanitized()
    {
        // 角标文字是操作员的内容，不该被 Sanitize 动。
        const string text = "主日崇拜 2026-08-23";

        var config = new AppConfig { Badge = new BadgeConfig { Text = text, Enabled = true } };

        AppConfig sanitized = config.Sanitize(out IReadOnlyList<string> corrections);

        Assert.Equal(text, sanitized.Badge.Text);
        Assert.True(sanitized.Badge.Enabled);
        Assert.Empty(corrections);
    }

    [Fact]
    public void CornersListHasExactlyFourEntries() => Assert.Equal(4, BadgeConfig.Corners.Count);
}
