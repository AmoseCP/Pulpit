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

    public LyricsConfig Lyrics { get; init; } = new();

    public BadgeConfig Badge { get; init; } = new();

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

        // System.Text.Json 不强制不可空注解：手编配置写出 "hotkeys": null 这类**合法 JSON**
        // 时，对应节就是 null——初始化器帮不上忙。§7 的契约是「非法就用默认值 + 写日志，
        // 永不抛」，所以先把缺失的节补成默认，再做逐字段夹值。
        T Section<T>(T? value, string name) where T : class, new()
        {
            if (value is not null)
            {
                return value;
            }

            notes.Add($"{name} 为 null，整节改用内置默认值");
            return new T();
        }

        BandConfig band = Section(Band, "band").Sanitize(notes);
        TypographyConfig typography = Section(Typography, "typography").Sanitize(notes);
        AnimationConfig animation = Section(Animation, "animation").Sanitize(notes);
        HotkeyConfig hotkeys = Section(Hotkeys, "hotkeys").Sanitize(notes);
        LyricsConfig lyrics = Section(Lyrics, "lyrics").Sanitize(notes);
        BadgeConfig badge = Section(Badge, "badge").Sanitize(notes);

        // TextConfig 没有可夹的字段，但同样可能整节为 null——它的 NRE 会延迟到
        // 直播中第一次投放时才爆，比启动崩溃更糟。
        TextConfig text = Section(Text, "text");

        corrections = notes;

        return this with
        {
            Band = band,
            Typography = typography,
            Animation = animation,
            Hotkeys = hotkeys,
            Text = text,
            Lyrics = lyrics,
            Badge = badge,
        };
    }
}

/// <summary>副屏带状区域的几何与底色。</summary>
public sealed record BandConfig
{
    /// <summary>带高占屏高的比例。L3：下三分之一，不全屏。</summary>
    public double HeightPercent { get; init; } = 0.30;

    /// <summary><c>bottom</c> / <c>top</c> / <c>center</c>。</summary>
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
            && !string.Equals(anchor, "top", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(anchor, "center", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"band.verticalAnchor「{VerticalAnchor}」无效（只许 bottom / top / center），改用 bottom");
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

/// <summary>
/// 副屏第二区域（P2-4）：角标 / 聚会主题常驻。
/// </summary>
/// <remarks>
/// 与正文带状区域**互不影响**：它是另一个窗口，有自己的位置、自己的显隐，
/// 正文淡入淡出不会碰它。默认放右上角——底部三分之一归正文（L3），别去挤它。
/// </remarks>
public sealed record BadgeConfig
{
    /// <summary>是否显示。默认关闭：不是每场聚会都要挂角标。</summary>
    public bool Enabled { get; init; }

    /// <summary>角标文字，如「主日崇拜」。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary><c>topRight</c> / <c>topLeft</c> / <c>bottomRight</c> / <c>bottomLeft</c>。</summary>
    public string Corner { get; init; } = "topRight";

    /// <summary>宽度占屏宽的比例。</summary>
    public double WidthPercent { get; init; } = 0.28;

    /// <summary>高度占屏高的比例。</summary>
    public double HeightPercent { get; init; } = 0.07;

    /// <summary>离屏幕边缘的留白，占屏幕短边的比例。</summary>
    public double MarginPercent { get; init; } = 0.02;

    /// <summary>底色不透明度。默认比正文带子淡一些——它是配角。</summary>
    public double BackgroundOpacity { get; init; } = 0.55;

    /// <summary>合法的四个角。</summary>
    public static IReadOnlyList<string> Corners { get; } =
        ["topRight", "topLeft", "bottomRight", "bottomLeft"];

    internal BadgeConfig Sanitize(List<string> notes)
    {
        string corner = Corner;
        bool known = false;

        foreach (string candidate in Corners)
        {
            if (string.Equals(candidate, corner, StringComparison.OrdinalIgnoreCase))
            {
                corner = candidate;
                known = true;
                break;
            }
        }

        if (!known)
        {
            notes.Add($"badge.corner「{Corner}」无效（只许 {string.Join(" ", Corners)}），改用 topRight");
            corner = "topRight";
        }

        return this with
        {
            Corner = corner,
            WidthPercent = Clamp(WidthPercent, 0.05, 1.00, 0.28, nameof(WidthPercent), notes),
            HeightPercent = Clamp(HeightPercent, 0.02, 0.30, 0.07, nameof(HeightPercent), notes),
            MarginPercent = Clamp(MarginPercent, 0.0, 0.20, 0.02, nameof(MarginPercent), notes),
            BackgroundOpacity = Clamp(BackgroundOpacity, 0.0, 1.0, 0.55, nameof(BackgroundOpacity), notes),
        };
    }

    private static double Clamp(
        double value, double min, double max, double fallback, string name, List<string> notes)
    {
        string field = "badge." + char.ToLowerInvariant(name[0]) + name[1..];

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            notes.Add($"{field} 不是有效数字，改用 {fallback}");
            return fallback;
        }

        if (value < min || value > max)
        {
            double clamped = value < min ? min : max;
            notes.Add($"{field}={value} 超出 [{min}, {max}]，夹到 {clamped}");
            return clamped;
        }

        return value;
    }
}

/// <summary>多行歌词模式（P2-2）。</summary>
public sealed record LyricsConfig
{
    /// <summary>
    /// 一页最多几行。空行本身就是分页点（小节边界），这个值只用来把
    /// 超长小节切开，免得一屏塞十行谁也看不清。
    /// </summary>
    public int LinesPerPage { get; init; } = 4;

    internal LyricsConfig Sanitize(List<string> notes)
    {
        if (LinesPerPage is < 1 or > 8)
        {
            notes.Add($"lyrics.linesPerPage={LinesPerPage} 超出 [1, 8]，改用 4");
            return this with { LinesPerPage = 4 };
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
