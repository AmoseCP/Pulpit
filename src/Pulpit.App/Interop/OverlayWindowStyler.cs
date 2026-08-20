using System;
using System.Windows.Threading;

namespace Pulpit.App.Interop;

/// <summary>
/// 负责叠加层窗口的 Win32 侧行为：扩展样式（L5）、2 秒置顶心跳（L6）、
/// 按物理像素定位到目标屏下三分之一带状区域（L3）。
/// </summary>
/// <remarks>
/// **为什么用物理像素 + SetWindowPos 而不是 WPF 的 Left/Top/Width/Height**：
/// 在 PerMonitorV2 下，WPF 的 Left/Top 是以「窗口当前所在监视器的 DPI」换算的
/// 设备无关单位，跨 DPI 移动窗口时会出现算错一轮的经典问题。
/// <see cref="System.Windows.Forms.Screen.Bounds"/> 给的是物理像素，
/// 直接 SetWindowPos 没有任何换算，跨 DPI 也不会错。
/// </remarks>
internal sealed class OverlayWindowStyler : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _heartbeat;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _disposed;

    /// <summary>心跳已执行次数，供控制窗口显示（M0 验收第 2/3 项排查用）。</summary>
    internal long HeartbeatCount { get; private set; }

    /// <summary>最近一次生效的扩展样式，供控制窗口自检显示。</summary>
    internal int CurrentExStyle { get; private set; }

    internal OverlayWindowStyler()
    {
        _heartbeat = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = HeartbeatInterval,
        };
        _heartbeat.Tick += OnHeartbeat;
    }

    /// <summary>
    /// 必须在窗口的 SourceInitialized 里调用——此时 HWND 已存在但还未首次呈现，
    /// 样式在第一帧之前就位，不会有「先亮一下再变透明」的闪烁。
    /// </summary>
    internal void AttachTo(IntPtr hwnd)
    {
        _hwnd = hwnd;
        ApplyExtendedStyles();
        _heartbeat.Start();
    }

    /// <summary>L5：四个扩展样式缺一不可，另外必须清掉 WS_EX_APPWINDOW。</summary>
    private void ApplyExtendedStyles()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = NativeMethods.GetWindowExStyle(_hwnd);

        exStyle |= NativeMethods.WS_EX_LAYERED
                 | NativeMethods.WS_EX_TRANSPARENT
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_NOACTIVATE;

        exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

        NativeMethods.SetWindowExStyle(_hwnd, exStyle);
        CurrentExStyle = NativeMethods.GetWindowExStyle(_hwnd);
    }

    /// <summary>
    /// 自检：四个必需位是否都在。控制窗口显示这个结果，
    /// 免得现场靠肉眼猜「穿透到底生效了没有」。
    /// </summary>
    internal bool VerifyExtendedStyles(out string report)
    {
        int s = CurrentExStyle;
        bool layered = (s & NativeMethods.WS_EX_LAYERED) != 0;
        bool transparent = (s & NativeMethods.WS_EX_TRANSPARENT) != 0;
        bool toolWindow = (s & NativeMethods.WS_EX_TOOLWINDOW) != 0;
        bool noActivate = (s & NativeMethods.WS_EX_NOACTIVATE) != 0;
        bool appWindow = (s & NativeMethods.WS_EX_APPWINDOW) != 0;

        report = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "LAYERED={0} TRANSPARENT={1} TOOLWINDOW={2} NOACTIVATE={3} APPWINDOW={4} (0x{5:X8})",
            Mark(layered), Mark(transparent), Mark(toolWindow), Mark(noActivate),
            appWindow ? "有(异常)" : "无", s);

        return layered && transparent && toolWindow && noActivate && !appWindow;

        static string Mark(bool ok) => ok ? "✓" : "✗";
    }

    /// <summary>
    /// 定位到目标屏。<paramref name="heightPercent"/> 为屏高占比，
    /// <paramref name="verticalAnchor"/> 取 <c>bottom</c> / <c>top</c> / <c>center</c> /
    /// <c>fullscreen</c>（config.band.verticalAnchor，已经过 Sanitize）。
    /// L3 默认仍是带状；fullscreen 是 2026-08-20 修订新增的可选档，覆盖整屏并忽略
    /// <paramref name="heightPercent"/>——软件渲染下全屏淡入较重，逃生口是 fadeMs=0。
    /// </summary>
    internal void PositionOnScreen(
        System.Windows.Forms.Screen screen, double heightPercent, string verticalAnchor = "bottom")
    {
        if (_hwnd == IntPtr.Zero || screen is null)
        {
            return;
        }

        System.Drawing.Rectangle b = screen.Bounds;   // 物理像素
        bool fullscreen = string.Equals(verticalAnchor, "fullscreen", StringComparison.Ordinal);

        int height = fullscreen ? b.Height : (int)Math.Round(b.Height * heightPercent);
        if (height < 1)
        {
            height = 1;
        }

        int x = b.Left;
        int y = verticalAnchor switch
        {
            "top" or "fullscreen" => b.Top,
            "center" => b.Top + (b.Height - height) / 2,
            _ => b.Bottom - height,
        };

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, x, y, b.Width, height,
            NativeMethods.SWP_NOACTIVATE);

        LastBand = new System.Drawing.Rectangle(x, y, b.Width, height);
    }

    /// <summary>最近一次定位得到的带状区域（物理像素），供控制窗口显示。</summary>
    internal System.Drawing.Rectangle LastBand { get; private set; }

    /// <summary>
    /// P2-4：定位到目标屏的某个角（角标 / 聚会主题常驻）。
    /// </summary>
    /// <remarks>
    /// 和 <see cref="PositionOnScreen"/> 一样走物理像素，理由相同（跨 DPI 不算错）。
    /// 留白按**屏幕短边**算：按长边算的话竖屏与横屏的观感会差很远。
    /// </remarks>
    internal void PositionInCorner(
        System.Windows.Forms.Screen screen,
        string corner,
        double widthPercent,
        double heightPercent,
        double marginPercent)
    {
        if (_hwnd == IntPtr.Zero || screen is null)
        {
            return;
        }

        System.Drawing.Rectangle b = screen.Bounds;   // 物理像素

        int width = Math.Max(1, (int)Math.Round(b.Width * widthPercent));
        int height = Math.Max(1, (int)Math.Round(b.Height * heightPercent));
        int margin = (int)Math.Round(Math.Min(b.Width, b.Height) * marginPercent);

        bool right = corner.EndsWith("Right", StringComparison.OrdinalIgnoreCase);
        bool bottom = corner.StartsWith("bottom", StringComparison.OrdinalIgnoreCase);

        int x = right ? b.Right - width - margin : b.Left + margin;
        int y = bottom ? b.Bottom - height - margin : b.Top + margin;

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE);

        LastBand = new System.Drawing.Rectangle(x, y, width, height);
    }

    /// <summary>L6：对抗放映软件的 Z 序抢占。成本近零，必须保留。</summary>
    internal void ForceTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    private void OnHeartbeat(object? sender, EventArgs e)
    {
        ForceTopmost();
        HeartbeatCount++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _heartbeat.Stop();
        _heartbeat.Tick -= OnHeartbeat;
    }
}
