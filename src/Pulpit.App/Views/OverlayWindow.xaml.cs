using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pulpit.App.Diagnostics;
using Pulpit.App.Interop;
using Pulpit.Core.Config;
using Pulpit.Core.Content;

namespace Pulpit.App.Views;

/// <summary>
/// 副屏透明叠加层。
/// </summary>
/// <remarks>
/// L4：本窗口生命周期与进程一致，**从不 Close**。「清屏」= 内容淡出，窗口继续存在。
/// <see cref="OnClosing"/> 会拦掉一切非进程退出的关闭请求。
/// </remarks>
public partial class OverlayWindow : Window, IOverlayController
{
    private readonly OverlayWindowStyler _styler = new();
    private readonly Stopwatch _fadeClock = new();

    private OverlayTheme _theme;
    private DisplayContent? _content;

    private bool _allowClose;
    private bool _fitting;
    private bool _measuringFade;
    private int _fadeFrames;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private string? _targetScreenDeviceName;

    /// <summary>
    /// 一次淡入/淡出的实测帧数据。M0 验收第 7 项要求回报「实测帧感」——
    /// 靠肉眼说「好像有点卡」没法做 go/no-go 决策，所以直接数帧。
    /// </summary>
    public event EventHandler<FadeMeasurement>? FadeMeasured;

    /// <summary>内容或页码发生变化。控制窗口据此刷新预览与状态栏。</summary>
    public event EventHandler? ContentChanged;

    public OverlayWindow(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        InitializeComponent();

        _theme = OverlayTheme.From(config, out IReadOnlyList<string> notes);
        foreach (string note in notes)
        {
            AppLog.Warn("配置项被修正：" + note);
        }

        _targetScreenDeviceName = config.TargetScreenDeviceName;

        ApplyTheme();

        RootLayer.SizeChanged += (_, _) => ApplyGeometry();
        BodyHost.SizeChanged += (_, _) => FitBody();
    }

    /// <summary>供控制窗口做预览用的可视根（VisualBrush 的源）。</summary>
    internal Visual PreviewSource => RootLayer;

    /// <summary>
    /// 窗口句柄。M2 验收要求「反复 Show/Clear 200 次，窗口句柄不变」——
    /// 控制窗口把这个值在压力测试前后各读一次做对比。
    /// </summary>
    public IntPtr WindowHandle { get; private set; }

    public bool IsContentVisible { get; private set; }

    public long HeartbeatCount => _styler.HeartbeatCount;

    public System.Drawing.Rectangle Band => _styler.LastBand;

    public string TargetScreenDeviceName => _targetScreenDeviceName ?? "(未定)";

    public DisplayContent? CurrentContent => _content;

    /// <summary>最近一次二分搜索得到的正文字号，供控制窗口自检显示。</summary>
    public double CurrentBodyFontSize { get; private set; }

    public bool VerifyWindowStyles(out string report) => _styler.VerifyExtendedStyles(out report);

    // ================= 主题与几何 =================

    /// <summary>换配置后原地重套主题，不重建窗口（L4）。</summary>
    public void ApplyConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _theme = OverlayTheme.From(config, out IReadOnlyList<string> notes);
        foreach (string note in notes)
        {
            AppLog.Warn("配置项被修正：" + note);
        }

        ApplyTheme();
        Reposition();
        ApplyGeometry();
        FitBody();
    }

    private void ApplyTheme()
    {
        BandBackground.Background = _theme.BandBackground;

        BodyText.FontFamily = _theme.FontFamily;
        BodyText.FontWeight = _theme.FontWeight;
        BodyText.Foreground = _theme.Foreground;

        LabelText.FontFamily = _theme.FontFamily;
        LabelText.FontWeight = _theme.FontWeight;
        LabelText.Foreground = _theme.Foreground;

        IndicatorText.FontFamily = _theme.FontFamily;
        IndicatorText.FontWeight = _theme.FontWeight;
        IndicatorText.Foreground = _theme.Foreground;
    }

    /// <summary>内边距按带状区域尺寸的百分比算：横向吃宽度，纵向吃高度。</summary>
    /// <remarks>
    /// §7 只给了一个 <c>paddingPercent</c>。若横纵都按高度算，1920×324 的带子
    /// 左右只留 19px，字几乎贴边；所以横向按宽度算（6% → 115px），纵向按高度算。
    /// </remarks>
    private void ApplyGeometry()
    {
        double w = RootLayer.ActualWidth;
        double h = RootLayer.ActualHeight;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        double padX = Math.Round(w * _theme.PaddingPercent);
        double padY = Math.Round(h * _theme.PaddingPercent);
        BandPadding.Margin = new Thickness(padX, padY, padX, padY);
    }

    // ================= IOverlayController =================

    public void Show(DisplayContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;

        // IsContentVisible 要在 RenderCurrentPage 之前置位——RenderCurrentPage 会触发
        // ContentChanged，控制窗口的「副屏当前为空」提示是照这个标志判断的。
        IsContentVisible = true;

        RenderCurrentPage();
        _styler.ForceTopmost();   // 淡入这一刻最需要确保在最上层
        Fade(to: 1.0);
    }

    /// <summary>L4：清屏只是淡出 + 置空，窗口继续存在。</summary>
    public void Clear()
    {
        IsContentVisible = false;
        Fade(to: 0.0);

        // 只丢内容模型，**不清 TextBlock 的文字**——文字要留在原地跟着透明度淡出去。
        // 立刻把文字清空会让最后 250ms 淡出的是一片空白，观感上等于瞬切。
        // 透明度归零后它就不可见了，下次 Show 会整体覆盖。
        _content = null;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool NextPage()
    {
        if (_content is null || !_content.TryNext())
        {
            return false;
        }

        RenderCurrentPage();
        return true;
    }

    public bool PrevPage()
    {
        if (_content is null || !_content.TryPrevious())
        {
            return false;
        }

        RenderCurrentPage();
        return true;
    }

    // ================= 渲染 =================

    private void RenderCurrentPage()
    {
        Page? page = _content?.Current;

        if (page is null)
        {
            BodyText.Text = string.Empty;
            LabelText.Text = string.Empty;
            IndicatorText.Text = string.Empty;
            FooterRow.Height = new GridLength(0);
            ContentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        BodyText.Text = page.Body;
        LabelText.Text = page.Label;
        IndicatorText.Text = _content!.PageIndicator;   // 单页时为空串（M2：只有多页才显示）

        bool hasFooter = page.Label.Length > 0 || IndicatorText.Text.Length > 0;

        // 固定预留高度，不用 Auto——理由见 XAML 里的注释（打断字号↔行高的循环依赖）。
        FooterRow.Height = new GridLength(
            hasFooter ? Math.Ceiling(_theme.MaxFontSize * _theme.LabelScale * 1.6) : 0);

        // 行高固定预留后，BodyHost 的高度与字号无关，可以直接同步布局再二分。
        BandPadding.UpdateLayout();
        FitBody();

        ContentChanged?.Invoke(this, EventArgs.Empty);
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
    private void FitBody()
    {
        if (_fitting)
        {
            return;
        }

        double availableWidth = BodyHost.ActualWidth;
        double availableHeight = BodyHost.ActualHeight;

        if (availableWidth <= 1 || availableHeight <= 1)
        {
            return;
        }

        string text = BodyText.Text;
        if (text.Length == 0)
        {
            return;
        }

        _fitting = true;

        try
        {
            double max = _theme.MaxFontSize;
            double min = _theme.MinFontSize;

            if (MeasureBodyHeight(text, max, availableWidth) <= availableHeight)
            {
                // 短内容走这条：字号被 MaxFontSize 限制，不会撑满整条带（M2 验收）。
                SetFontSizes(max);
                return;
            }

            if (MeasureBodyHeight(text, min, availableWidth) > availableHeight)
            {
                // 连下限都放不下。宁可用下限（会被 ClipToBounds 裁掉尾部）也不再缩，
                // 缩到看不见等于没投。这种情况该在日志里留痕。
                SetFontSizes(min);
                AppLog.Warn(
                    $"正文在最小字号 {min} 下仍超出带状区域（{availableWidth:F0}×{availableHeight:F0}），" +
                    $"内容 {text.Length} 字，尾部会被裁切。");
                return;
            }

            double lo = min;
            double hi = max;

            // 0.5px 精度足够：再细的差别在投影上看不出来。
            for (int i = 0; i < 16 && hi - lo > 0.5; i++)
            {
                double mid = (lo + hi) / 2;

                if (MeasureBodyHeight(text, mid, availableWidth) <= availableHeight)
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

    /// <summary>用 <see cref="FormattedText"/> 算给定字号下换行后的总高度。</summary>
    private double MeasureBodyHeight(string text, double fontSize, double maxWidth)
    {
        double lineHeight = fontSize * OverlayTheme.LineHeightFactor;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            _theme.Typeface,
            fontSize,
            _theme.Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            LineHeight = lineHeight,
            Trimming = TextTrimming.None,
            TextAlignment = TextAlignment.Center,
        };

        return formatted.Height;
    }

    private void SetFontSizes(double bodySize)
    {
        CurrentBodyFontSize = bodySize;

        BodyText.FontSize = bodySize;
        BodyText.LineHeight = bodySize * OverlayTheme.LineHeightFactor;

        double labelSize = Math.Max(1, bodySize * _theme.LabelScale);
        LabelText.FontSize = labelSize;
        IndicatorText.FontSize = labelSize;
    }

    // ================= 淡入淡出 =================

    private void Fade(double to)
    {
        // fadeMs = 0 → 无动画直切。这是 M0 验收第 7 项的逃生口：若软件渲染下
        // 淡入实测帧率过低，改配置即可退化为直切，不动这里的代码。
        if (_theme.FadeMs <= 0)
        {
            RootLayer.BeginAnimation(UIElement.OpacityProperty, null);
            RootLayer.Opacity = to;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(_theme.FadeMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        animation.Completed += (_, _) => StopFadeMeasurement();
        StartFadeMeasurement();

        RootLayer.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void StartFadeMeasurement()
    {
        if (_measuringFade)
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        _measuringFade = true;
        _fadeFrames = 0;
        _lastRenderingTime = TimeSpan.MinValue;
        _fadeClock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopFadeMeasurement()
    {
        if (!_measuringFade)
        {
            return;
        }

        _measuringFade = false;
        CompositionTarget.Rendering -= OnRendering;
        _fadeClock.Stop();

        double ms = _fadeClock.Elapsed.TotalMilliseconds;
        double fps = ms > 0 ? _fadeFrames * 1000.0 / ms : 0;

        FadeMeasured?.Invoke(this, new FadeMeasurement(_fadeFrames, ms, fps));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering 在同一帧可能触发多次，按 RenderingTime 去重才是真帧数。
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime)
            {
                return;
            }

            _lastRenderingTime = args.RenderingTime;
        }

        _fadeFrames++;
    }

    // ================= 窗口生命周期与定位 =================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        WindowHandle = hwnd;

        // 顺序要紧：先打样式，再定位。样式在首次呈现之前就位，不会闪一下。
        _styler.AttachTo(hwnd);
        Reposition();
    }

    /// <summary>L4：拦掉一切关闭请求，只有 <see cref="AllowCloseOnShutdown"/> 之后才放行。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>仅由 <c>App</c> 在进程退出时调用。</summary>
    internal void AllowCloseOnShutdown()
    {
        _allowClose = true;
        _styler.Dispose();
    }

    /// <summary>L14：换屏或系统缩放变化后重新按物理像素定位，否则带状区域会算错。</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    /// <summary>
    /// 按设备名找回目标屏；找不到就取第一块非主屏；只有一块屏时退回主屏
    /// （单屏是开发机场景，现场一定是双屏）。
    /// </summary>
    private System.Windows.Forms.Screen ResolveTargetScreen()
    {
        System.Windows.Forms.Screen[] all = System.Windows.Forms.Screen.AllScreens;

        if (_targetScreenDeviceName is not null)
        {
            foreach (System.Windows.Forms.Screen s in all)
            {
                if (string.Equals(s.DeviceName, _targetScreenDeviceName, StringComparison.Ordinal))
                {
                    return s;
                }
            }

            AppLog.Warn($"目标屏 {_targetScreenDeviceName} 不在当前屏幕列表中，改用第一块非主屏。");
        }

        foreach (System.Windows.Forms.Screen s in all)
        {
            if (!s.Primary)
            {
                return s;
            }
        }

        return System.Windows.Forms.Screen.PrimaryScreen ?? all[0];
    }

    public void MoveToScreen(System.Windows.Forms.Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        _targetScreenDeviceName = screen.DeviceName;
        _styler.PositionOnScreen(screen, _theme.HeightPercent, _theme.AnchorBottom);
    }

    /// <summary>P0-13：显示器变更后重新定位。副屏拔出时会退回主屏而不是崩。</summary>
    public void Reposition()
    {
        System.Windows.Forms.Screen screen = ResolveTargetScreen();
        _targetScreenDeviceName = screen.DeviceName;
        _styler.PositionOnScreen(screen, _theme.HeightPercent, _theme.AnchorBottom);
    }
}

/// <summary>一次淡入淡出的实测结果。</summary>
public readonly record struct FadeMeasurement(int Frames, double ElapsedMs, double Fps);
