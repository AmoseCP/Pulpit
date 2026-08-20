using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Pulpit.Core.Config;

namespace Pulpit.App.Views;

/// <summary>
/// 把 <see cref="AppConfig"/> 里的字符串/数值映射成 WPF 类型。
/// </summary>
/// <remarks>
/// 这一层存在的理由是 <c>Pulpit.Core</c> 不得引用任何 WPF 类型——颜色、字重、
/// 字体族的解析必须发生在 App 层。解析失败一律退回默认值并记一条说明，
/// 绝不抛异常：配置文件写坏了不是停机理由（§7）。
/// </remarks>
internal sealed class OverlayTheme
{
    /// <summary>行高系数。正文与出处标签都用它，测量与呈现共用同一个数才对得上。</summary>
    internal const double LineHeightFactor = 1.35;

    private OverlayTheme(
        FontFamily fontFamily,
        FontWeight fontWeight,
        Typeface typeface,
        Brush foreground,
        Brush bandBackground,
        double maxFontSize,
        double minFontSize,
        double labelScale,
        double paddingPercent,
        double heightPercent,
        string verticalAnchor,
        int fadeMs)
    {
        FontFamily = fontFamily;
        FontWeight = fontWeight;
        Typeface = typeface;
        Foreground = foreground;
        BandBackground = bandBackground;
        MaxFontSize = maxFontSize;
        MinFontSize = minFontSize;
        LabelScale = labelScale;
        PaddingPercent = paddingPercent;
        HeightPercent = heightPercent;
        VerticalAnchor = verticalAnchor;
        FadeMs = fadeMs;
    }

    internal FontFamily FontFamily { get; }

    internal FontWeight FontWeight { get; }

    /// <summary>供 <see cref="FormattedText"/> 测量使用，必须与呈现用的字体族/字重一致。</summary>
    internal Typeface Typeface { get; }

    internal Brush Foreground { get; }

    internal Brush BandBackground { get; }

    internal double MaxFontSize { get; }

    internal double MinFontSize { get; }

    internal double LabelScale { get; }

    internal double PaddingPercent { get; }

    internal double HeightPercent { get; }

    /// <summary><c>bottom</c> / <c>top</c> / <c>center</c>，已经过 Sanitize。</summary>
    internal string VerticalAnchor { get; }

    /// <summary>0 表示无动画直切（见 <see cref="AnimationConfig.FadeMs"/>）。</summary>
    internal int FadeMs { get; }

    internal static OverlayTheme From(AppConfig config, out IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(config);

        var messages = new List<string>();

        TypographyConfig t = config.Typography;
        BandConfig b = config.Band;

        var family = new FontFamily(t.FontFamily);
        FontWeight weight = ParseFontWeight(t.FontWeight, messages);
        Brush foreground = ParseBrush(t.Foreground, Colors.White, "typography.foreground", messages);

        // 半透明黑底：不透明度直接进 alpha 通道。
        var background = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(b.BackgroundOpacity * 255), 0, 0, 0));

        // 冻结能省掉每帧的可变性检查——分层窗口是软件渲染，这点开销值得省。
        foreground.Freeze();
        background.Freeze();

        notes = messages;

        return new OverlayTheme(
            fontFamily: family,
            fontWeight: weight,
            typeface: new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
            foreground: foreground,
            bandBackground: background,
            maxFontSize: t.MaxFontSize,
            minFontSize: t.MinFontSize,
            labelScale: t.LabelScale,
            paddingPercent: b.PaddingPercent,
            heightPercent: b.HeightPercent,
            verticalAnchor: b.VerticalAnchor.ToLowerInvariant(),
            fadeMs: config.Animation.FadeMs);
    }

    private static FontWeight ParseFontWeight(string name, List<string> notes)
    {
        try
        {
            object? parsed = new FontWeightConverter().ConvertFromString(name);

            if (parsed is FontWeight weight)
            {
                return weight;
            }
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            // 落到下面的默认值。
        }

        notes.Add($"typography.fontWeight「{name}」无法解析，改用 SemiBold");
        return FontWeights.SemiBold;
    }

    private static Brush ParseBrush(string value, Color fallback, string field, List<string> notes)
    {
        try
        {
            object? parsed = ColorConverter.ConvertFromString(value);
            if (parsed is Color color)
            {
                return new SolidColorBrush(color);
            }
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            // 落到下面的默认值。
        }

        notes.Add($"{field}「{value}」无法解析，改用 #FFFFFFFF");
        return new SolidColorBrush(fallback);
    }
}
