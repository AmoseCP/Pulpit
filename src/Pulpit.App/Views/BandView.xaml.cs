using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Pulpit.App.Diagnostics;

namespace Pulpit.App.Views;

/// <summary>
/// 带状区域本体：主题套用、几何内边距、字号二分搜索、单页渲染。
/// </summary>
/// <remarks>
/// 副屏（<see cref="OverlayWindow"/>）与控制窗口的「投放前预览」共用本控件——
/// 同一份结构、同一套字号算法，预览与副屏不可能各画各的。
/// 本控件只管「一页长什么样」；内容模型、翻页、淡入淡出、置顶与定位都归宿主。
/// </remarks>
public partial class BandView : System.Windows.Controls.UserControl
{
    private OverlayTheme? _theme;
    private bool _fitting;

    public BandView()
    {
        InitializeComponent();

        SizeChanged += (_, _) => ApplyGeometry();
        BodyHost.SizeChanged += (_, _) => FitBody();
    }

    /// <summary>最近一次二分搜索得到的正文字号，供控制窗口自检显示。</summary>
    public double CurrentBodyFontSize { get; private set; }

    /// <summary>
    /// 正文放不下时是否写告警日志。副屏实例保持 true；
    /// 预览实例设为 false——同一份内容会双份渲染，日志里报两遍是噪音。
    /// </summary>
    internal bool LogOverflow { get; set; } = true;

    /// <summary>套主题并立即重算几何与字号。换配置后由宿主再调一次渲染。</summary>
    internal void ApplyTheme(OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _theme = theme;

        BandBackground.Background = theme.BandBackground;

        foreach (System.Windows.Controls.TextBlock block in AllTextBlocks())
        {
            block.FontFamily = theme.FontFamily;
            block.FontWeight = theme.FontWeight;
            block.Foreground = theme.Foreground;
        }

        ApplyGeometry();
        FitBody();
    }

    private System.Windows.Controls.TextBlock[] AllTextBlocks() =>
        [SecondaryText, SecondaryLabelText, BodyText, BodyLabelText, LabelText, IndicatorText];

    /// <summary>
    /// 渲染一页。<paramref name="page"/> 为 null 清空所有文字（页脚行也收起）。
    /// </summary>
    /// <remarks>
    /// 两种布局按 <see cref="Pulpit.Core.Content.Page.SecondaryBody"/> 是否为空自动选：
    /// 单段落页出处走页脚右下角（原有形态）；中英对照页四块堆叠——
    /// 英文正文 + 英文出处、中文正文 + 中文出处，各自成组、出处随文右对齐，
    /// 页脚只留页码指示。
    /// </remarks>
    internal void Render(Pulpit.Core.Content.Page? page, string indicator)
    {
        if (_theme is null)
        {
            return;
        }

        if (page is null)
        {
            foreach (System.Windows.Controls.TextBlock block in AllTextBlocks())
            {
                block.Text = string.Empty;
            }

            SecondaryText.Visibility = Visibility.Collapsed;
            SecondaryLabelText.Visibility = Visibility.Collapsed;
            BodyLabelText.Visibility = Visibility.Collapsed;
            FooterRow.Height = new GridLength(0);
            return;
        }

        bool bilingual = page.SecondaryBody.Length > 0;

        BodyText.Text = page.Body;
        SecondaryText.Text = page.SecondaryBody;
        SecondaryLabelText.Text = page.SecondaryLabel;
        BodyLabelText.Text = bilingual ? page.Label : string.Empty;

        SecondaryText.Visibility = bilingual ? Visibility.Visible : Visibility.Collapsed;
        SecondaryLabelText.Visibility = bilingual && page.SecondaryLabel.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        BodyLabelText.Visibility = bilingual && page.Label.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 对照页出处已随文显示，页脚只留页码；单段落页出处照旧走页脚右下角。
        LabelText.Text = bilingual ? string.Empty : page.Label;
        IndicatorText.Text = indicator;

        bool hasFooter = LabelText.Text.Length > 0 || indicator.Length > 0;

        // 固定预留高度，不用 Auto——理由见 XAML 里的注释（打断字号↔行高的循环依赖）。
        FooterRow.Height = new GridLength(
            hasFooter ? Math.Ceiling(_theme.MaxFontSize * _theme.LabelScale * 1.6) : 0);

        // 行高固定预留后，BodyHost 的高度与字号无关，可以直接同步布局再二分。
        BandPadding.UpdateLayout();
        FitBody();
    }

    /// <summary>内边距按带状区域尺寸的百分比算：横向吃宽度，纵向吃高度。</summary>
    /// <remarks>
    /// §7 只给了一个 <c>paddingPercent</c>。若横纵都按高度算，1920×324 的带子
    /// 左右只留 19px，字几乎贴边；所以横向按宽度算（6% → 115px），纵向按高度算。
    /// </remarks>
    private void ApplyGeometry()
    {
        if (_theme is null)
        {
            return;
        }

        double w = ActualWidth;
        double h = ActualHeight;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        double padX = Math.Round(w * _theme.PaddingPercent);
        double padY = Math.Round(h * _theme.PaddingPercent);
        BandPadding.Margin = new Thickness(padX, padY, padX, padY);
    }

    /// <summary>
    /// 二分搜索能把正文完整放进 <c>BodyHost</c> 的最大字号。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用 Viewbox</b>：Viewbox 给子元素无限宽度，<c>TextWrapping</c>
    /// 永不生效；给 TextBlock 固定宽度再让 Viewbox 等比缩，换行点是在
    /// <c>MaxFontSize</c> 下算出来的，缩小后行数偏多、字号偏小。实测 106 字的
    /// 申30:9 在 1920×324 带子里，Viewbox 方案约 35px，按最优换行可到约 46px——
    /// 30% 的字号差，副屏后排能不能看清就在这里。</para>
    /// <para><b>为什么用 FormattedText 测量</b>：它不进视觉树，不触发布局，
    /// 因此不可能与正在进行的布局过程互相重入。代价是行高模型必须自己对齐——
    /// 测量与呈现都用 <see cref="OverlayTheme.LineHeightFactor"/>，
    /// TextBlock 侧配 <c>LineStackingStrategy=BlockLineHeight</c>，两边就一致了。</para>
    /// </remarks>
    internal void FitBody()
    {
        if (_fitting || _theme is null)
        {
            return;
        }

        double availableWidth = BodyHost.ActualWidth;
        double availableHeight = BodyHost.ActualHeight;

        if (availableWidth <= 1 || availableHeight <= 1)
        {
            return;
        }

        if (BodyText.Text.Length == 0 && SecondaryText.Text.Length == 0)
        {
            return;
        }

        _fitting = true;

        try
        {
            double max = _theme.MaxFontSize;
            double min = _theme.MinFontSize;

            if (MeasureStackHeight(max, availableWidth) <= availableHeight)
            {
                // 短内容走这条：字号被 MaxFontSize 限制，不会撑满整条带（M2 验收）。
                SetFontSizes(max);
                return;
            }

            if (MeasureStackHeight(min, availableWidth) > availableHeight)
            {
                // 连下限都放不下。宁可用下限（会被 ClipToBounds 裁掉尾部）也不再缩，
                // 缩到看不见等于没投。这种情况该在日志里留痕。
                SetFontSizes(min);

                if (LogOverflow)
                {
                    int length = BodyText.Text.Length + SecondaryText.Text.Length;
                    AppLog.Warn(
                        $"正文在最小字号 {min} 下仍超出带状区域（{availableWidth:F0}×{availableHeight:F0}），" +
                        $"内容 {length} 字，尾部会被裁切。");
                }

                return;
            }

            double lo = min;
            double hi = max;

            // 0.5px 精度足够：再细的差别在投影上看不出来。
            for (int i = 0; i < 16 && hi - lo > 0.5; i++)
            {
                double mid = (lo + hi) / 2;

                if (MeasureStackHeight(mid, availableWidth) <= availableHeight)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            SetFontSizes(lo);
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>
    /// 给定正文字号下整个堆叠（正文 + 随文出处，单段落时只有正文）换行后的总高度。
    /// 出处块按 <see cref="OverlayTheme.LabelScale"/> 缩小后的字号参与测量——
    /// 二分的目标是「整组」放进 BodyHost，不是单独某一块。
    /// </summary>
    private double MeasureStackHeight(double fontSize, double maxWidth)
    {
        double labelSize = Math.Max(1, fontSize * _theme!.LabelScale);
        double height = 0;

        if (SecondaryText.Visibility == Visibility.Visible && SecondaryText.Text.Length > 0)
        {
            height += MeasureBodyHeight(SecondaryText.Text, fontSize, maxWidth);
        }

        if (SecondaryLabelText.Visibility == Visibility.Visible && SecondaryLabelText.Text.Length > 0)
        {
            height += MeasureBodyHeight(SecondaryLabelText.Text, labelSize, maxWidth);
        }

        if (BodyText.Text.Length > 0)
        {
            height += MeasureBodyHeight(BodyText.Text, fontSize, maxWidth);
        }

        if (BodyLabelText.Visibility == Visibility.Visible && BodyLabelText.Text.Length > 0)
        {
            height += MeasureBodyHeight(BodyLabelText.Text, labelSize, maxWidth);
        }

        return height;
    }

    /// <summary>用 <see cref="FormattedText"/> 算给定字号下换行后的总高度。</summary>
    private double MeasureBodyHeight(string text, double fontSize, double maxWidth)
    {
        double lineHeight = fontSize * OverlayTheme.LineHeightFactor;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            _theme!.Typeface,
            fontSize,
            _theme.Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            LineHeight = lineHeight,
            Trimming = TextTrimming.None,
            // 与 XAML 的 BodyText 保持一致（整体居中、行内左对齐）。
            // 对齐不影响换行点与总高度，但两边不一致迟早引人误判。
            TextAlignment = TextAlignment.Left,
        };

        return formatted.Height;
    }

    private void SetFontSizes(double bodySize)
    {
        CurrentBodyFontSize = bodySize;

        double lineHeight = bodySize * OverlayTheme.LineHeightFactor;
        BodyText.FontSize = bodySize;
        BodyText.LineHeight = lineHeight;
        SecondaryText.FontSize = bodySize;
        SecondaryText.LineHeight = lineHeight;

        double labelSize = Math.Max(1, bodySize * _theme!.LabelScale);
        double labelLineHeight = labelSize * OverlayTheme.LineHeightFactor;
        SecondaryLabelText.FontSize = labelSize;
        SecondaryLabelText.LineHeight = labelLineHeight;
        BodyLabelText.FontSize = labelSize;
        BodyLabelText.LineHeight = labelLineHeight;
        LabelText.FontSize = labelSize;
        IndicatorText.FontSize = labelSize;
    }
}
