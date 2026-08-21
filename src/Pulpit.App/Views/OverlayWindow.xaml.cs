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

        // 几何（内边距）与字号随尺寸变化的重算都由 BandView 自己盯着。
        BandArea.ApplyTheme(_theme);
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
    public double CurrentBodyFontSize => BandArea.CurrentBodyFontSize;

    /// <summary>
    /// 带状区域的 DIP 尺寸。② 预览用它把「投放前」的渲染面做成与副屏同宽高——
    /// 尺寸一致换行点才一致。窗口尚未定位时为 0，调用方自备回退值。
    /// </summary>
    internal Size BandDipSize => new(RootLayer.ActualWidth, RootLayer.ActualHeight);

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

        BandArea.ApplyTheme(_theme);   // 内部会重算内边距与字号
        Reposition();

        // 必须走完整的 RenderCurrentPage 而不是只重算字号：
        // 页脚预留高度是按 MaxFontSize 算的，只重算字号会留下一个按旧上限预留的页脚。
        if (_content is not null)
        {
            RenderCurrentPage();
        }
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
        // 单页时页码指示器为空串（M2：只有多页才显示）。
        BandArea.Render(_content?.Current, _content?.PageIndicator ?? string.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
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
    /// <remarks>
    /// 定位必须**延后**执行：PerMonitorV2 下 WPF 在本方法返回后还会用系统建议矩形
    /// 再做一次 SetWindowPos（旧矩形按 DPI 比例缩放的结果）。在方法体里直接
    /// Reposition 会被那次覆盖——带子落在错误的位置/尺寸上，且程序化跨屏移动
    /// 不触发 DisplaySettingsChanged，没有任何东西会来纠正它。
    /// </remarks>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, Reposition);
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
        _styler.PositionOnScreen(screen, _theme.HeightPercent, _theme.VerticalAnchor);
    }

    /// <summary>P0-13：显示器变更后重新定位。副屏拔出时会退回主屏而不是崩。</summary>
    /// <remarks>
    /// **不得**把回退结果写回 <c>_targetScreenDeviceName</c>：那等于用主屏覆盖操作员
    /// 配置的目标屏——投影线重新插回后按名字能找到「目标屏」（已被改成主屏），
    /// 带子就永远留在操作员屏幕上了。目标名只在 <see cref="MoveToScreen"/>
    /// （操作员显式选择）里改；回退只影响这一次的落点。
    /// </remarks>
    public void Reposition()
    {
        System.Windows.Forms.Screen screen = ResolveTargetScreen();
        _styler.PositionOnScreen(screen, _theme.HeightPercent, _theme.VerticalAnchor);
    }
}

/// <summary>一次淡入淡出的实测结果。</summary>
public readonly record struct FadeMeasurement(int Frames, double ElapsedMs, double Fps);
