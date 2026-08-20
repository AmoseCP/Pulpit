using System;
using System.Collections.Generic;

namespace Pulpit.Core.Config;

/// <summary>
/// 应用配置，对应 <c>%LOCALAPPDATA%\Pulpit\config.json</c>（DEVELOPMENT_PLAN §7）。
/// </summary>
/// <remarks>
/// <para>用属性初始化器而不是位置参数：配置文件可能只写了其中几个字段，
/// System.Text.Json 遇到缺失属性会保留初始化器给的默认值，正好实现
/// 「配置缺失或字段非法时用内置默认值」。位置记录做不到这点——嵌套记录
/// 无法作为编译期常量默认值。</para>
/// <para>本类型刻意只用 <c>string</c>/<c>double</c>/<c>bool</c>：
/// 颜色、字重、键位到 WPF/Win32 类型的映射发生在 App 层，
/// Core 不引用任何 WPF 类型。</para>
/// </remarks>
public sealed record AppConfig
{
    /// <summary>目标屏设备名，如 <c>\\.\DISPLAY2</c>。null 表示自动选第一块非主屏。</summary>
    public string? TargetScreenDeviceName { get; init; }

    public BandConfig Band { get; init; } = new();

    public TypographyConfig Typography { get; init; } = new();

    public AnimationConfig Animation { get; init; } = new();

    public HotkeyConfig Hotkeys { get; init; } = new();

    public TextConfig Text { get; init; } = new();

    /// <summary>
    /// 把越界或非法的字段夹回合法范围，返回被修正项的说明（供调用方写日志）。
    /// </summary>
    /// <remarks>
    /// §7 要求「配置缺失或字段非法时用内置默认值，并写日志，**不弹窗**」。
    /// 所以这里只夹值、只回报，永不抛异常。
    /// </remarks>
    public AppConfig Sanitize(out IReadOnlyList<string> corrections)
    {
        var notes = new List<string>();

        BandConfig band = Band.Sanitize(notes);
        TypographyConfig typography = Typography.Sanitize(notes);
        AnimationConfig animation = Animation.Sanitize(notes);
        HotkeyConfig hotkeys = Hotkeys.Sanitize(notes);

        corrections = notes;

        return this with
        {
            Band = band,
            Typography = typography,
            Animation = animation,
            Hotkeys = hotkeys,
        };
    }
}

/// <summary>副屏带状区域的几何与底色。</summary>
public sealed record BandConfig
{
    /// <summary>带高占屏高的比例。L3：下三分之一，不全屏。</summary>
    public double HeightPercent { get; init; } = 0.30;

    /// <summary><c>bottom</c> 或 <c>top</c>。</summary>
    public string VerticalAnchor { get; init; } = "bottom";

    /// <summary>黑底不透明度。0 = 全透明（只剩白字），1 = 纯黑。</summary>
    public double BackgroundOpacity { get; init; } = 0.72;

    /// <summary>内边距占带高的比例。</summary>
    public double PaddingPercent { get; init; } = 0.06;

    internal BandConfig Sanitize(List<string> notes)
    {
        double height = Clamp(HeightPercent, 0.10, 1.00, 0.30, nameof(HeightPercent), notes);
        double opacity = Clamp(BackgroundOpacity, 0.0, 1.0, 0.72, nameof(BackgroundOpacity), notes);
        double padding = Clamp(PaddingPercent, 0.0, 0.30, 0.06, nameof(PaddingPercent), notes);

        string anchor = VerticalAnchor;
        if (!string.Equals(anchor, "bottom", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(anchor, "top", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"band.verticalAnchor「{VerticalAnchor}」无效，改用 bottom");
            anchor = "bottom";
        }

        return this with
        {
            HeightPercent = height,
            VerticalAnchor = anchor.ToLowerInvariant(),
            BackgroundOpacity = opacity,
            PaddingPercent = padding,
        };
    }

    private static double Clamp(
        double value, double min, double max, double fallback, string name, List<string> notes)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            notes.Add($"band.{Camel(name)} 不是有效数字，改用 {fallback}");
            return fallback;
        }

        if (value < min || value > max)
        {
            double clamped = value < min ? min : max;
            notes.Add($"band.{Camel(name)}={value} 超出 [{min}, {max}]，夹到 {clamped}");
            return clamped;
        }

        return value;
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>正文与出处标签的排版。</summary>
public sealed record TypographyConfig
{
    public string FontFamily { get; init; } = "Microsoft YaHei UI";

    /// <summary>WPF FontWeight 名，如 <c>Normal</c> / <c>SemiBold</c> / <c>Bold</c>。</summary>
    public string FontWeight { get; init; } = "SemiBold";

    /// <summary>
    /// 正文字号上限。M2 验收：最短内容（2 字）字号被这个值限制，不会撑满整条带。
    /// </summary>
    public double MaxFontSize { get; init; } = 96;

    /// <summary>字号下限。低于此值即使溢出也不再缩——太小了后排根本看不见。</summary>
    public double MinFontSize { get; init; } = 24;

    /// <summary>出处标签字号 = 正文字号 × 此比例。</summary>
    public double LabelScale { get; init; } = 0.40;

    /// <summary>ARGB 十六进制，如 <c>#FFFFFFFF</c>。</summary>
    public string Foreground { get; init; } = "#FFFFFFFF";

    internal TypographyConfig Sanitize(List<string> notes)
    {
        double max = MaxFontSize;
        double min = MinFontSize;
        double scale = LabelScale;

        if (double.IsNaN(max) || max < 8 || max > 400)
        {
            notes.Add($"typography.maxFontSize={MaxFontSize} 无效，改用 96");
            max = 96;
        }

        if (double.IsNaN(min) || min < 4 || min > max)
        {
            notes.Add($"typography.minFontSize={MinFontSize} 无效（须 ≤ maxFontSize），改用 {Math.Min(24, max)}");
            min = Math.Min(24, max);
        }

        if (double.IsNaN(scale) || scale <= 0 || scale > 1)
        {
            notes.Add($"typography.labelScale={LabelScale} 无效，改用 0.40");
            scale = 0.40;
        }

        string family = FontFamily;
        if (string.IsNullOrWhiteSpace(family))
        {
            notes.Add("typography.fontFamily 为空，改用 Microsoft YaHei UI");
            family = "Microsoft YaHei UI";
        }

        return this with
        {
            FontFamily = family,
            MaxFontSize = max,
            MinFontSize = min,
            LabelScale = scale,
        };
    }
}

/// <summary>淡入淡出。</summary>
public sealed record AnimationConfig
{
    /// <summary>
    /// 淡入淡出时长（毫秒）。**0 表示无动画直切。**
    /// </summary>
    /// <remarks>
    /// 这个 0 分支是 M0 验收第 7 项的逃生口：<c>AllowsTransparency=true</c> 关闭了
    /// 该窗口的硬件加速，若实测淡入帧率过低（&lt; 30fps），把这里设成 0 就退化为直切，
    /// 不需要改动 <c>OverlayWindow</c> 的任何代码。
    /// </remarks>
    public int FadeMs { get; init; } = 250;

    internal AnimationConfig Sanitize(List<string> notes)
    {
        if (FadeMs < 0 || FadeMs > 5000)
        {
            notes.Add($"animation.fadeMs={FadeMs} 超出 [0, 5000]，改用 250");
            return this with { FadeMs = 250 };
        }

        return this;
    }
}

/// <summary>正文取清洗版还是原貌。</summary>
public sealed record TextConfig
{
    /// <summary>true 用 <c>text_raw</c>（含敬空与译注），默认 false 用 <c>text_display</c>。</summary>
    public bool UseRawText { get; init; }
}
