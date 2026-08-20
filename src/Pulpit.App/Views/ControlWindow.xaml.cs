using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulpit.App.Diagnostics;

namespace Pulpit.App.Views;

/// <summary>
/// M0 尖刺的控制窗口。只做两件事：驱动叠加层，以及把「靠肉眼猜不准」的指标显示出来
/// （扩展样式位、带状区域物理像素、淡入实测帧率、内存增长）。
/// </summary>
/// <remarks>
/// M0 **刻意不注册任何全局热键**。L7 规定只有 F7/F8/F9/F10/F12 可注册，而 M0 要验的是
/// 窗口行为本身；提前注册热键只会给「键位是否误抢 PPT 翻页」这个问题引入无关变量。
/// 热键子系统属于 M4。
/// </remarks>
public partial class ControlWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _poll;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly long _baselineWorkingSet;

    private FadeMeasurement _lastFade;
    private bool _hasFadeSample;

    /// <summary>M0 验收标准，逐字取自 DEVELOPMENT_PLAN.md §3 M0。</summary>
    private static readonly string[] Checklist =
    [
        "1. WPS 全屏放映时，文字可见",
        "2. 连续翻页 20 次，文字始终可见",
        "3. 播放带动画的页面，文字不闪烁、不消失",
        "4. 鼠标点击文字区域，事件落到 WPS 而非本程序（穿透生效）",
        "5. Alt+Tab 列表中不出现本程序",
        "6. 主屏切换到记事本并打字，WPS 未退出全屏，文字仍在",
        "7. 淡入淡出 250ms 视觉流畅，无卡顿撕裂（并记录下方实测帧率）",
        "8. OBS 显示器采集能抓到文字",
        "9. 连续运行 60 分钟，内存无明显增长（看下方内存增量）",
    ];

    public ControlWindow(OverlayWindow overlay)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        InitializeComponent();

        using (Process self = Process.GetCurrentProcess())
        {
            _baselineWorkingSet = self.WorkingSet64;
        }

        _overlay.FadeMeasured += OnFadeMeasured;

        BuildChecklist();
        RefreshScreens();
        LogPathText.Text = "日志：" + AppLog.CurrentLogPath;

        _poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _poll.Tick += (_, _) => RefreshDiagnostics();
        _poll.Start();

        RefreshDiagnostics();
    }

    private void BuildChecklist()
    {
        foreach (string item in Checklist)
        {
            ChecklistPanel.Children.Add(new CheckBox
            {
                Margin = new Thickness(0, 3, 0, 3),
                Content = new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap },
            });
        }
    }

    // ---------- 屏幕 ----------

    /// <summary>ComboBox 用的一行；<see cref="ToString"/> 就是显示文本。</summary>
    private sealed record ScreenChoice(System.Windows.Forms.Screen Screen)
    {
        public override string ToString()
        {
            System.Drawing.Rectangle b = Screen.Bounds;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}  {2}×{3}  @({4},{5})",
                Screen.DeviceName,
                Screen.Primary ? " [主屏]" : " [副屏]",
                b.Width, b.Height, b.Left, b.Top);
        }
    }

    private void RefreshScreens()
    {
        var choices = new List<ScreenChoice>();
        int selected = 0;

        System.Windows.Forms.Screen[] all = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < all.Length; i++)
        {
            choices.Add(new ScreenChoice(all[i]));

            if (string.Equals(all[i].DeviceName, _overlay.TargetScreenDeviceName, StringComparison.Ordinal))
            {
                selected = i;
            }
        }

        ScreenList.ItemsSource = choices;
        if (choices.Count > 0)
        {
            ScreenList.SelectedIndex = selected;
        }
    }

    private void OnRefreshScreens(object sender, RoutedEventArgs e)
    {
        RefreshScreens();
        _overlay.Reposition();
        AppLog.Info($"手动刷新屏幕列表，共 {System.Windows.Forms.Screen.AllScreens.Length} 块屏。");
    }

    private void OnMoveToScreen(object sender, RoutedEventArgs e)
    {
        if (ScreenList.SelectedItem is ScreenChoice choice)
        {
            _overlay.MoveToScreen(choice.Screen);
            AppLog.Info($"叠加层移到 {choice.Screen.DeviceName}。");
            RefreshDiagnostics();
        }
    }

    // ---------- 投放 ----------

    private void OnApplyText(object sender, RoutedEventArgs e)
    {
        _overlay.Body = string.IsNullOrWhiteSpace(BodyInput.Text) ? "测试" : BodyInput.Text;
    }

    private void OnFadeIn(object sender, RoutedEventArgs e)
    {
        OnApplyText(sender, e);
        _overlay.FadeIn();
    }

    private void OnFadeOut(object sender, RoutedEventArgs e) => _overlay.FadeOut();

    /// <summary>验收第 2/3 项的辅助：连续 20 轮淡入淡出，观察是否掉帧或丢 Z 序。</summary>
    private async void OnStressFade(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;

            try
            {
                for (int i = 0; i < 20; i++)
                {
                    _overlay.FadeIn();
                    await Task.Delay(500).ConfigureAwait(true);
                    _overlay.FadeOut();
                    await Task.Delay(500).ConfigureAwait(true);
                }

                AppLog.Info("连续淡入淡出 ×20 完成。");
            }
            catch (Exception ex)
            {
                AppLog.Error("连续淡入淡出压力测试异常。", ex);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    // ---------- 自检显示 ----------

    private void OnFadeMeasured(object? sender, FadeMeasurement m)
    {
        _lastFade = m;
        _hasFadeSample = true;
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        bool stylesOk = _overlay.VerifyWindowStyles(out string styleReport);
        StyleReport.Text = (stylesOk ? "扩展样式 正常  " : "扩展样式 异常  ") + styleReport;

        System.Drawing.Rectangle band = _overlay.Band;
        BandReport.Text = string.Format(
            CultureInfo.InvariantCulture,
            "目标屏 {0}   带状区域 {1}×{2} @({3},{4}) 物理像素   心跳 {5} 次",
            _overlay.TargetScreenDeviceName,
            band.Width, band.Height, band.Left, band.Top,
            _overlay.HeartbeatCount);

        FadeReport.Text = _hasFadeSample
            ? string.Format(
                CultureInfo.InvariantCulture,
                "上次淡入淡出 {0} 帧 / {1:F0}ms → {2:F1} fps{3}",
                _lastFade.Frames, _lastFade.ElapsedMs, _lastFade.Fps,
                _lastFade.Fps < 30 ? "   ← 低于 30fps，按验收第 7 项回报" : string.Empty)
            : "上次淡入淡出 —（还没投放过）";

        long workingSet;
        using (Process self = Process.GetCurrentProcess())
        {
            workingSet = self.WorkingSet64;
        }

        RuntimeReport.Text = string.Format(
            CultureInfo.InvariantCulture,
            "运行 {0:hh\\:mm\\:ss}   工作集 {1:F1} MB（基线 {2:F1} MB，增量 {3:+0.0;-0.0;0.0} MB）   托管堆 {4:F1} MB",
            _uptime.Elapsed,
            workingSet / 1048576.0,
            _baselineWorkingSet / 1048576.0,
            (workingSet - _baselineWorkingSet) / 1048576.0,
            GC.GetTotalMemory(false) / 1048576.0);
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _overlay.FadeMeasured -= OnFadeMeasured;
        base.OnClosed(e);
    }
}
