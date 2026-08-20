using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pulpit.App.Diagnostics;
using Pulpit.Core.Config;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;

namespace Pulpit.App.Views;

/// <summary>
/// M2 阶段的控制窗口：驱动叠加层的测试台 + 把「靠肉眼猜不准」的指标显示出来。
/// </summary>
/// <remarks>
/// M2 的产出是「<c>OverlayWindow</c> 完整实现，由测试用的按钮驱动」，所以这里还没有
/// 模式指示、预览区、IME 安全——那些是 M3。全局热键是 M4，此处按钮上的
/// 「F7/F8/F12」只是标注键位归属，尚未注册。
/// </remarks>
public partial class ControlWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly IBibleRepository? _repository;
    private readonly IReferenceParser? _parser;
    private readonly AppConfig _config;
    private readonly DispatcherTimer _poll;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly long _baselineWorkingSet;

    private FadeMeasurement _lastFade;
    private bool _hasFadeSample;

    public ControlWindow(
        OverlayWindow overlay,
        IBibleRepository? repository,
        IReferenceParser? parser,
        AppConfig config,
        string? databaseError)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _repository = repository;
        _parser = parser;
        _config = config ?? throw new ArgumentNullException(nameof(config));

        InitializeComponent();

        using (Process self = Process.GetCurrentProcess())
        {
            _baselineWorkingSet = self.WorkingSet64;
        }

        _overlay.FadeMeasured += OnFadeMeasured;
        _overlay.ContentChanged += OnOverlayContentChanged;

        if (databaseError is not null)
        {
            DatabaseWarning.Visibility = Visibility.Visible;
            DatabaseWarningText.Text =
                $"经文库不可用，只能投放自由文本。{databaseError}";
        }

        InputBox.TextChanged += (_, _) => RefreshMode();

        RefreshScreens();
        RefreshMode();
        LogPathText.Text = "日志：" + AppLog.CurrentLogPath;

        _poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _poll.Tick += (_, _) => RefreshDiagnostics();
        _poll.Start();

        RefreshDiagnostics();
        InputBox.Focus();
    }

    // ================= 投放 =================

    /// <summary>
    /// 解析输入并投放。三态语义（§5）在这里落地：
    /// 解析成功走经文，<c>error</c> 为 null 走自由文本，<c>error</c> 非空则**只报错不投放**。
    /// </summary>
    private void OnSend(object sender, RoutedEventArgs e) => Send(InputBox.Text);

    private void Send(string input)
    {
        DisplayContent? content = BuildContent(input, out string? error);

        if (error is not null)
        {
            // P0-10：报错只出现在控制窗口，副屏保持原状，绝不上副屏。
            ModeText.Text = "✗ " + error;
            ModeText.Foreground = System.Windows.Media.Brushes.Firebrick;
            AppLog.Info($"投放被拒：{input} → {error}");
            return;
        }

        if (content is null)
        {
            return;
        }

        _overlay.Show(content);
        AppLog.Info($"投放：{input}（{content.Kind}，{content.PageCount} 页）");
        RefreshDiagnostics();
    }

    /// <summary>
    /// 把输入变成待投内容。返回 null 且 <paramref name="error"/> 非空 = 该报错；
    /// 返回 null 且 error 为 null = 没有可投的东西（空输入）。
    /// </summary>
    private DisplayContent? BuildContent(string input, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (_parser is not null && _repository is not null
            && _parser.TryParse(input, out VerseRef? reference, out error))
        {
            IReadOnlyList<VerseText> verses = _repository.Lookup(reference);

            if (verses.Count == 0)
            {
                // 解析通过、章节也在范围内，却查不到文本——库出了问题，不该静默上屏。
                error = "该节在库中查不到文本";
                return null;
            }

            return ContentBuilder.FromVerses(reference, verses, _config.Text.UseRawText);
        }

        if (error is not null)
        {
            return null;
        }

        // 不是引用格式 → 原样上屏（P0-4）。
        return ContentBuilder.FromFreeText(input);
    }

    private void OnSample(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sample })
        {
            InputBox.Text = sample;
            Send(sample);
        }
    }

    private void OnPrevPage(object sender, RoutedEventArgs e)
    {
        if (!_overlay.PrevPage())
        {
            FlashStatus("已在首页");
        }
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        if (!_overlay.NextPage())
        {
            FlashStatus("已在末页");
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _overlay.Clear();
        RefreshDiagnostics();
    }

    // ================= 模式指示（M3 会做成实时判定的完整版）=================

    private void RefreshMode()
    {
        string input = InputBox.Text;

        if (string.IsNullOrWhiteSpace(input))
        {
            ModeText.Text = "输入经文引用（约3:16）或任意文字（自由文本）";
            ModeText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        if (_parser is null)
        {
            ModeText.Text = "自由文本（经文库不可用）";
            ModeText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            return;
        }

        if (_parser.TryParse(input, out VerseRef? reference, out string? error))
        {
            (int Chapters, string NameZh)? info = _repository?.GetBookInfo(reference.BookId);
            string range = reference.EndVerse is null
                ? $"{reference.Chapter}:{reference.Verse}"
                : $"{reference.Chapter}:{reference.Verse}-{reference.EndVerse}";

            ModeText.Text = $"经文 → {info?.NameZh ?? "?"} {range}";
            ModeText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            return;
        }

        if (error is not null)
        {
            ModeText.Text = "✗ " + error;
            ModeText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        ModeText.Text = "自由文本 → 原样上屏";
        ModeText.Foreground = System.Windows.Media.Brushes.DarkBlue;
    }

    private void FlashStatus(string message)
    {
        ModeText.Text = message;
        ModeText.Foreground = System.Windows.Media.Brushes.DarkOrange;
    }

    // ================= 屏幕 =================

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
        RefreshDiagnostics();
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

    // ================= 压力测试 =================

    /// <summary>
    /// M2 验收：反复 Show/Clear 200 次，窗口句柄不变、Z 序不丢。
    /// </summary>
    /// <remarks>
    /// 刻意用 20ms 的间隔而不是等每次淡入淡出走完——那样 200 轮要 100 秒，
    /// 而且**互相打断的动画本身才是更狠的压力**（BeginAnimation 覆盖进行中的动画）。
    /// </remarks>
    private async void OnStress(object sender, RoutedEventArgs e)
    {
        StressButton.IsEnabled = false;

        IntPtr before = _overlay.WindowHandle;
        long heartbeatBefore = _overlay.HeartbeatCount;

        try
        {
            DisplayContent content = BuildContent("测试", out _) ?? ContentBuilder.FromFreeText("测试");

            for (int i = 0; i < 200; i++)
            {
                _overlay.Show(content);
                await Task.Delay(20).ConfigureAwait(true);
                _overlay.Clear();
                await Task.Delay(20).ConfigureAwait(true);

                if (i % 20 == 0)
                {
                    StressReport.Text = $"进行中… {i}/200";
                }
            }

            IntPtr after = _overlay.WindowHandle;
            bool stylesOk = _overlay.VerifyWindowStyles(out string styleReport);

            StressReport.Text = string.Format(
                CultureInfo.InvariantCulture,
                "200 轮完成。\n句柄 前=0x{0:X} 后=0x{1:X} → {2}\n扩展样式 → {3}\n{4}\n心跳 {5} → {6} 次",
                before.ToInt64(), after.ToInt64(),
                before == after ? "不变 ✓" : "已改变 ✗（L4 被违反）",
                stylesOk ? "完好 ✓" : "异常 ✗",
                styleReport,
                heartbeatBefore, _overlay.HeartbeatCount);

            AppLog.Info("Show/Clear ×200 压力测试完成。" + StressReport.Text.Replace('\n', ' '));
        }
        catch (Exception ex)
        {
            AppLog.Error("压力测试异常。", ex);
            StressReport.Text = "压力测试异常，详见日志。";
        }
        finally
        {
            StressButton.IsEnabled = true;
        }
    }

    // ================= 自检显示 =================

    private void OnFadeMeasured(object? sender, FadeMeasurement m)
    {
        _lastFade = m;
        _hasFadeSample = true;
    }

    private void OnOverlayContentChanged(object? sender, EventArgs e) => RefreshDiagnostics();

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

        DisplayContent? content = _overlay.CurrentContent;
        RenderReport.Text = string.Format(
            CultureInfo.InvariantCulture,
            "当前内容 {0}   页 {1}   正文字号 {2:F1}px（上限 {3:F0}）   DB {4}",
            content is null ? "(空)" : content.Kind.ToString(),
            content is null ? "-" : $"{content.Index + 1}/{content.PageCount}",
            _overlay.CurrentBodyFontSize,
            _config.Typography.MaxFontSize,
            _repository is null ? "不可用" : "已加载");

        FadeReport.Text = _hasFadeSample
            ? string.Format(
                CultureInfo.InvariantCulture,
                "上次淡入淡出 {0} 帧 / {1:F0}ms → {2:F1} fps{3}",
                _lastFade.Frames, _lastFade.ElapsedMs, _lastFade.Fps,
                _lastFade.Fps < 30 ? "   ← 低于 30fps，考虑把 animation.fadeMs 设为 0（直切）" : string.Empty)
            : _config.Animation.FadeMs == 0
                ? "淡入淡出已关闭（animation.fadeMs=0，直切）"
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
        _overlay.ContentChanged -= OnOverlayContentChanged;
        base.OnClosed(e);
    }
}
