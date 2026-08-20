using System;
using System.Collections.Generic;
using System.IO;
using Pulpit.Core.Config;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// <see cref="ConfigStore"/> 的核心契约：**任何输入都不得抛异常给调用方**。
/// 直播前十分钟配置文件被编辑器写坏，程序也必须能起来（§7）。
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pulpit-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ConfigStore Store() => new(_path);

    // ---------------- §7 里那份配置必须真的能解析 ----------------

    /// <summary>
    /// 逐字取自 DEVELOPMENT_PLAN §7 的示例配置。文档里写着的东西解析不了是最难查的 bug，
    /// 所以把它钉成用例。
    /// </summary>
    [Fact]
    public void ThePlannedExampleConfigParses()
    {
        File.WriteAllText(_path, """
            {
              "targetScreenDeviceName": "\\\\.\\DISPLAY2",
              "band": {
                "heightPercent": 0.30,
                "verticalAnchor": "bottom",
                "backgroundOpacity": 0.72,
                "paddingPercent": 0.06
              },
              "typography": {
                "fontFamily": "Microsoft YaHei UI",
                "fontWeight": "SemiBold",
                "maxFontSize": 96,
                "labelScale": 0.40,
                "foreground": "#FFFFFFFF"
              },
              "animation": { "fadeMs": 250 },
              "hotkeys": {
                "sendZh": "F9", "sendEn": "F10",
                "prevPage": "F7", "nextPage": "F8",
                "clear": "F12"
              },
              "text": { "useRawText": false }
            }
            """);

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Empty(notes);
        Assert.Equal(@"\\.\DISPLAY2", config.TargetScreenDeviceName);
        Assert.Equal(0.30, config.Band.HeightPercent);
        Assert.Equal(0.72, config.Band.BackgroundOpacity);
        Assert.Equal("Microsoft YaHei UI", config.Typography.FontFamily);
        Assert.Equal(96, config.Typography.MaxFontSize);
        Assert.Equal(0.40, config.Typography.LabelScale);
        Assert.Equal(250, config.Animation.FadeMs);
        Assert.Equal("F9", config.Hotkeys.SendZh);
        Assert.False(config.Text.UseRawText);
    }

    // ---------------- 缺失 / 损坏 / 部分 ----------------

    [Fact]
    public void MissingFileYieldsDefaultsAndWritesOneOut()
    {
        Assert.False(File.Exists(_path));

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Equal(new AppConfig(), config);
        Assert.NotEmpty(notes);
        Assert.True(File.Exists(_path), "首次运行应写出一份默认配置，好让操作员有可编辑的起点");
    }

    [Fact]
    public void MalformedJsonYieldsDefaultsWithoutThrowing()
    {
        File.WriteAllText(_path, "{ 这不是 JSON ]]}");

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Equal(new AppConfig(), config);
        Assert.NotEmpty(notes);
    }

    [Fact]
    public void EmptyFileYieldsDefaults()
    {
        File.WriteAllText(_path, "   ");

        Assert.Equal(new AppConfig(), Store().Load(out IReadOnlyList<string> notes));
        Assert.NotEmpty(notes);
    }

    [Fact]
    public void JsonNullYieldsDefaults()
    {
        File.WriteAllText(_path, "null");

        Assert.Equal(new AppConfig(), Store().Load(out _));
    }

    /// <summary>
    /// 只写了一个字段的配置文件，其余字段必须保持内置默认值——
    /// 这是「用属性初始化器而不是位置记录」换来的性质。
    /// </summary>
    [Fact]
    public void PartialConfigKeepsDefaultsForEverythingElse()
    {
        File.WriteAllText(_path, """{ "band": { "heightPercent": 0.25 } }""");

        AppConfig config = Store().Load(out _);

        Assert.Equal(0.25, config.Band.HeightPercent);
        Assert.Equal("bottom", config.Band.VerticalAnchor);       // 未写 → 默认
        Assert.Equal(0.72, config.Band.BackgroundOpacity);        // 未写 → 默认
        Assert.Equal(96, config.Typography.MaxFontSize);          // 整节未写 → 默认
        Assert.Equal("F9", config.Hotkeys.SendZh);
        Assert.Equal(250, config.Animation.FadeMs);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        File.WriteAllText(_path, """
            {
              // 手工编辑的人会写注释
              "animation": { "fadeMs": 0 },
            }
            """);

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Equal(0, config.Animation.FadeMs);
        Assert.Empty(notes);
    }

    // ---------------- 非法值走 Sanitize，报告到 notes ----------------

    [Fact]
    public void IllegalValuesAreSanitizedAndReported()
    {
        File.WriteAllText(_path, """
            {
              "band": { "heightPercent": 99, "verticalAnchor": "middle" },
              "animation": { "fadeMs": -1 }
            }
            """);

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Equal(1.0, config.Band.HeightPercent);
        Assert.Equal("bottom", config.Band.VerticalAnchor);
        Assert.Equal(250, config.Animation.FadeMs);
        Assert.Equal(3, notes.Count);
    }

    /// <summary>
    /// L7 端到端：把方向键写进真正的 config.json，读出来必须是 F8 而不是 Right。
    /// 这一条红了就意味着现场可能出现「操作员翻不了 PPT」。
    /// </summary>
    [Fact]
    public void ArrowKeyInFileIsRejectedOnLoad()
    {
        File.WriteAllText(_path, """
            { "hotkeys": { "nextPage": "Right", "prevPage": "PageUp", "sendZh": "Enter" } }
            """);

        AppConfig config = Store().Load(out IReadOnlyList<string> notes);

        Assert.Equal("F8", config.Hotkeys.NextPage);
        Assert.Equal("F7", config.Hotkeys.PrevPage);
        Assert.Equal("F9", config.Hotkeys.SendZh);
        Assert.Equal(3, notes.Count);
    }

    // ---------------- 写入 ----------------

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var original = new AppConfig
        {
            TargetScreenDeviceName = @"\\.\DISPLAY3",
            Band = new BandConfig { HeightPercent = 0.25, BackgroundOpacity = 0.5 },
            Typography = new TypographyConfig { MaxFontSize = 72, LabelScale = 0.35 },
            Animation = new AnimationConfig { FadeMs = 0 },
            Text = new TextConfig { UseRawText = true },
        };

        Assert.True(Store().TrySave(original, out string? error), error);

        AppConfig reloaded = Store().Load(out IReadOnlyList<string> notes);

        Assert.Empty(notes);
        Assert.Equal(original, reloaded);
    }

    [Fact]
    public void SaveUsesCamelCaseKeysSoTheFileMatchesTheDocumentedShape()
    {
        Store().TrySave(new AppConfig(), out _);

        string json = File.ReadAllText(_path);

        Assert.Contains("\"heightPercent\"", json, StringComparison.Ordinal);
        Assert.Contains("\"backgroundOpacity\"", json, StringComparison.Ordinal);
        Assert.Contains("\"maxFontSize\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fadeMs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sendZh\"", json, StringComparison.Ordinal);
        Assert.Contains("\"useRawText\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        Store().TrySave(new AppConfig(), out _);

        Assert.False(File.Exists(_path + ".tmp"), "临时文件应已被替换掉，不该留在目录里");
    }

    [Fact]
    public void SaveToAnUnwritablePathReportsInsteadOfThrowing()
    {
        // 让配置文件的**父目录**位置上已经存在一个同名文件：CreateDirectory 必然失败。
        string blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "占位");

        var store = new ConfigStore(Path.Combine(blocker, "config.json"));

        Assert.False(store.TrySave(new AppConfig(), out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void DefaultDirectoryIsUnderLocalAppData()
    {
        Assert.EndsWith("Pulpit", ConfigStore.DefaultDirectory, StringComparison.Ordinal);

        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ConfigStore.DefaultDirectory,
            StringComparison.Ordinal);
    }
}
