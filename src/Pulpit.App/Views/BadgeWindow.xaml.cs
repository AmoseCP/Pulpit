using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pulpit.App.Interop;
using Pulpit.Core.Config;

namespace Pulpit.App.Views;

/// <summary>
/// 副屏第二区域（P2-4）：角标 / 聚会主题常驻。
/// </summary>
/// <remarks>
/// <para>与 <see cref="OverlayWindow"/> 是**两个独立窗口**：各自定位、各自显隐，
/// 正文的淡入淡出碰不到角标，角标也不会跟着正文清屏而消失——「常驻」就是这个意思。</para>
/// <para>L4 同样适用：**从不 Close**。关掉的方式只有进程退出。</para>
/// <para>L5 的四个扩展样式、L6 的心跳，都复用 <see cref="OverlayWindowStyler"/>。
/// 两个窗口各有一个 2 秒心跳，它们在屏幕上不重叠（正文在底部三分之一，角标默认在右上角），
/// 所以彼此不会争 Z 序；它们要争的是放映软件。</para>
/// </remarks>
public partial class BadgeWindow : Window
{
    private const double FadeMilliseconds = 200;

    private readonly OverlayWindowStyler _styler = new();

    private BadgeConfig _badge;
    private OverlayTheme _theme;
    private bool _allowClose;
    private string? _targetScreenDeviceName;

    public BadgeWindow(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        InitializeComponent();

        _badge = config.Badge;
        _theme = OverlayTheme.From(config, out _);
        _targetScreenDeviceName = config.TargetScreenDeviceName;

        ApplyTheme();
    }

    /// <summary>当前是否在显示。</summary>
    public bool IsBadgeVisible { get; private set; }

    /// <summary>角标区域（物理像素），供控制窗口自检显示。</summary>
    public System.Drawing.Rectangle Region => _styler.LastBand;

    public bool VerifyWindowStyles(out string report) => _styler.VerifyExtendedStyles(out report);

    // ================= 生命周期 =================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        _styler.AttachTo(hwnd);
        Reposition();
    }

    /// <summary>L4：拦掉一切关闭请求。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    internal void AllowCloseOnShutdown()
    {
        _allowClose = true;
        _styler.Dispose();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    // ================= 配置与定位 =================

    public void ApplyConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _badge = config.Badge;
        _theme = OverlayTheme.From(config, out _);
        _targetScreenDeviceName = config.TargetScreenDeviceName;

        ApplyTheme();
        Reposition();

        // 配置里关掉了就淡出，开着且有文字就显示。
        if (_badge.Enabled && _badge.Text.Length > 0)
        {
            ShowBadge(_badge.Text);
        }
        else
        {
            HideBadge();
        }
    }

    private void ApplyTheme()
    {
        BadgeText.FontFamily = _theme.FontFamily;
        BadgeText.FontWeight = _theme.FontWeight;
        BadgeText.Foreground = _theme.Foreground;

        var background = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(_badge.BackgroundOpacity * 255), 0, 0, 0));

        background.Freeze();
        BadgeBackground.Background = background;
    }

    public void MoveToScreen(System.Windows.Forms.Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        _targetScreenDeviceName = screen.DeviceName;
        Reposition();
    }

    public void Reposition()
    {
        System.Windows.Forms.Screen screen = ResolveTargetScreen();
        _targetScreenDeviceName = screen.DeviceName;

        _styler.PositionInCorner(
            screen,
            _badge.Corner,
            _badge.WidthPercent,
            _badge.HeightPercent,
            _badge.MarginPercent);
    }

    /// <summary>与 <see cref="OverlayWindow"/> 同一套退让规则：目标屏没了就退回主屏，不崩。</summary>
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

    // ================= 显隐 =================

    public void ShowBadge(string text)
    {
        BadgeText.Text = text ?? string.Empty;

        if (BadgeText.Text.Length == 0)
        {
            HideBadge();
            return;
        }

        IsBadgeVisible = true;
        _styler.ForceTopmost();
        Fade(1.0);
    }

    /// <summary>L4：隐藏 = 淡出，窗口继续存在。</summary>
    public void HideBadge()
    {
        IsBadgeVisible = false;
        Fade(0.0);
    }

    private void Fade(double to)
    {
        // 与正文同一套逃生口：fadeMs=0 时直切，不做动画。
        if (_theme.FadeMs <= 0)
        {
            RootLayer.BeginAnimation(UIElement.OpacityProperty, null);
            RootLayer.Opacity = to;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(FadeMilliseconds)),
            FillBehavior = FillBehavior.HoldEnd,
        };

        RootLayer.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
