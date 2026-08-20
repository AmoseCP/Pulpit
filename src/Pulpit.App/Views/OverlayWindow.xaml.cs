using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pulpit.App.Interop;

namespace Pulpit.App.Views;

/// <summary>
/// 副屏透明叠加层。M0 尖刺版本：只显示一条文字，验证窗口行为本身。
/// </summary>
/// <remarks>
/// L4：本窗口生命周期与进程一致，**从不 Close**。「清屏」= 内容淡出，窗口继续存在。
/// <see cref="OnClosing"/> 会拦掉一切非进程退出的关闭请求。
/// </remarks>
public partial class OverlayWindow : Window
{
    /// <summary>L3：带状区域高度占屏高的比例（config.band.heightPercent 默认值）。</summary>
    private const double BandHeightPercent = 0.30;

    /// <summary>淡入淡出时长（config.animation.fadeMs 默认值）。</summary>
    private const double FadeMilliseconds = 250;

    private readonly OverlayWindowStyler _styler = new();
    private readonly Stopwatch _fadeClock = new();

    private bool _allowClose;
    private bool _measuringFade;
    private int _fadeFrames;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private string? _targetScreenDeviceName;

    /// <summary>
    /// 一次淡入/淡出实测到的帧数据。M0 验收第 7 项要求回报「实测帧感」，
    /// 靠肉眼说「好像有点卡」没法做决策，所以直接数帧。
    /// </summary>
    public event EventHandler<FadeMeasurement>? FadeMeasured;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>当前是否处于「有内容」状态（淡入过）。</summary>
    public bool IsContentVisible { get; private set; }

    public long HeartbeatCount => _styler.HeartbeatCount;

    public System.Drawing.Rectangle Band => _styler.LastBand;

    public bool VerifyWindowStyles(out string report) => _styler.VerifyExtendedStyles(out report);

    // ---------- 窗口生命周期 ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // 顺序要紧：先打样式，再定位。样式在首次呈现之前就位，不会闪一下。
        _styler.AttachTo(hwnd);

        System.Windows.Forms.Screen screen = ResolveTargetScreen();
        _targetScreenDeviceName = screen.DeviceName;
        _styler.PositionOnScreen(screen, BandHeightPercent);
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

    // ---------- 目标屏 ----------

    /// <summary>
    /// 按设备名找回上次的目标屏；找不到就取第一个非主屏；只有一块屏时退回主屏
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
        _styler.PositionOnScreen(screen, BandHeightPercent);
    }

    public void Reposition() => _styler.PositionOnScreen(ResolveTargetScreen(), BandHeightPercent);

    public string TargetScreenDeviceName => _targetScreenDeviceName ?? "(未定)";

    // ---------- 内容与淡入淡出 ----------

    public string Body
    {
        get => BodyText.Text;
        set => BodyText.Text = value;
    }

    /// <summary>P0-5 的前身：淡入 250ms。</summary>
    public void FadeIn()
    {
        IsContentVisible = true;
        _styler.ForceTopmost();   // 淡入这一刻最需要确保在最上层
        Animate(to: 1.0);
    }

    /// <summary>P0-6 的前身：淡出 250ms，**不 Hide、不 Close**（L4）。</summary>
    public void FadeOut()
    {
        IsContentVisible = false;
        Animate(to: 0.0);
    }

    private void Animate(double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(FadeMilliseconds)),
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
}

/// <summary>一次淡入淡出的实测结果。</summary>
public readonly record struct FadeMeasurement(int Frames, double ElapsedMs, double Fps);
