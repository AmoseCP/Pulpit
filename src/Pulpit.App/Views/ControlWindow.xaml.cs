using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pulpit.App.Diagnostics;
using Pulpit.Core.Config;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;

namespace Pulpit.App.Views;

/// <summary>
/// 主屏控制窗口。输入、预览、翻页、清屏、状态。
/// </summary>
/// <remarks>
/// <para><b>IME 安全（L8 + M3 验收）</b>是本类最要紧的性质，由三件事共同保证：</para>
/// <list type="number">
/// <item><c>InputBox.AcceptsReturn=False</c> —— TextBox 自己吞掉 Enter。</item>
/// <item>全窗口**没有任何** <c>IsDefault="True"</c> 的按钮，也没有绑到 Enter 的
///   <c>KeyBinding</c>、<c>PreviewKeyDown</c>。最安全的 Enter 处理就是一行都不写：
///   只要存在一个默认按钮，Enter 就会重新变成送出键，中文输入法确认候选词时
///   就会有半截内容上屏。</item>
/// <item>组合态跟踪（<see cref="TextCompositionManager"/>）—— 送出走的是全局热键 F9，
///   它可能在输入法**正在组合**的时刻到达，此时 <c>InputBox.Text</c> 里是半成品。
///   所以组合中一律拒绝送出并提示，见 <see cref="IsComposing"/>。</item>
/// </list>
/// </remarks>
public partial class ControlWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly IBibleRepository? _repository;
    private readonly IReferenceParser? _parser;
    private readonly AppConfig _config;
    private readonly string? _databaseVersion;
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
        string? databaseVersion,
        string? databaseError)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _repository = repository;
        _parser = parser;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _databaseVersion = databaseVersion;

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
            DatabaseWarningText.Text = $"经文库不可用，只能投放自由文本。{databaseError}";
        }

        AttachPreview();
        AttachImeTracking();

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

    /// <summary>操作员改了目标屏。<c>App</c> 据此把设备名写进配置（P0-12）。</summary>
    public event EventHandler? TargetScreenChanged;

    /// <summary>热键子系统的状态文本，由 <c>App</c> 在注册完成后写入。</summary>
    public string HotkeyStatus { get; set; } = "热键：未启用";

    /// <summary>输入法是否正在组合候选词。</summary>
    public bool IsComposing { get; private set; }

    // ================= 预览：所见即副屏 =================

    /// <summary>
    /// 用 <see cref="VisualBrush"/> 直接镜像叠加层的可视根。
    /// </summary>
    /// <remarks>
    /// 这不是「照着副屏的样式在主屏重画一遍」——那种做法迟早会与副屏漂移
    /// （改了字号规则忘了改预览）。VisualBrush 用的是同一份渲染结果，
    /// 结构上不可能不一致。淡出后预览也会跟着变空，那正是副屏的真实状态，
    /// 所以另配一行「副屏当前为空」的提示，免得操作员对着黑框发懵。
    /// </remarks>
    private void AttachPreview()
    {
        var brush = new VisualBrush(_overlay.PreviewSource)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };

        PreviewSurface.Fill = brush;
    }

    // ================= IME 组合态跟踪 =================

    private void AttachImeTracking()
    {
        InputBox.AddHandler(
            TextCompositionManager.TextInputStartEvent,
            new TextCompositionEventHandler(OnCompositionStart));

        InputBox.AddHandler(
            TextCompositionManager.TextInputUpdateEvent,
            new TextCompositionEventHandler(OnCompositionUpdate));

        InputBox.AddHandler(
            TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler(OnCompositionEnd));

        InputBox.LostKeyboardFocus += (_, _) => SetComposing(false);
    }

    private void OnCompositionStart(object sender, TextCompositionEventArgs e) => SetComposing(true);

    private void OnCompositionUpdate(object sender, TextCompositionEventArgs e) => SetComposing(true);

    /// <summary>TextInput 表示这一段文字已经确认落地，组合结束。</summary>
    private void OnCompositionEnd(object sender, TextCompositionEventArgs e) => SetComposing(false);

    private void SetComposing(bool composing)
    {
        if (IsComposing == composing)
        {
            return;
        }

        IsComposing = composing;
        RefreshMode();
        RefreshStatusBar();
    }

    // ================= 投放 =================

    private void OnSend(object sender, RoutedEventArgs e) => SendCurrentInput();

    /// <summary>
    /// 送出当前输入。由「投放」按钮和 M4 的 F9 全局热键共用同一条路径。
    /// </summary>
    public void SendCurrentInput()
    {
        if (IsComposing)
        {
            // F9 可能在输入法组合中到达，此时 InputBox.Text 是半成品。
            // 宁可不投也不能投半截（L8 的同一个道理）。
            ShowMode("输入法正在组合候选词，请先确认后再送出", ModeLevel.Warning);
            AppLog.Info("送出被拒：输入法组合中。");
            return;
        }

        Send(InputBox.Text);
    }

    /// <summary>P0-9：F10 键位在 v1 必须存在，只是提示英文库未安装。</summary>
    public void SendEnglish()
    {
        // L13：v1 仅中文，但键位必须在 v1 就建立起志愿者的肌肉记忆。
        ShowMode("英文译本未安装（v1.1 补）", ModeLevel.Warning);
        AppLog.Info("F10 被按下，但英文译本未安装。");
    }

    private void OnSendEnglish(object sender, RoutedEventArgs e) => SendEnglish();

    private void Send(string input)
    {
        DisplayContent? content = BuildContent(input, out string? error);

        if (error is not null)
        {
            // P0-10：报错只出现在控制窗口，副屏保持原状，绝不上副屏。
            ShowMode("✗ " + error, ModeLevel.Error);
            AppLog.Info($"投放被拒：{input} → {error}");
            return;
        }

        if (content is null)
        {
            ShowMode("没有可投放的内容", ModeLevel.Hint);
            return;
        }

        _overlay.Show(content);
        AppLog.Info($"投放：{input}（{content.Kind}，{content.PageCount} 页）");
        RefreshDiagnostics();
    }

    /// <summary>
    /// 把输入变成待投内容。返回 null 且 <paramref name="error"/> 非空 = 该报错；
    /// 两者都为 null = 没有可投的东西（空输入）。
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

    private void OnPrevPage(object sender, RoutedEventArgs e) => PrevPage();

    private void OnNextPage(object sender, RoutedEventArgs e) => NextPage();

    private void OnClear(object sender, RoutedEventArgs e) => Clear();

    public void PrevPage()
    {
        if (!_overlay.PrevPage())
        {
            ShowMode("已在首页", ModeLevel.Hint);
        }
    }

    public void NextPage()
    {
        if (!_overlay.NextPage())
        {
            ShowMode("已在末页", ModeLevel.Hint);
        }
    }

    public void Clear()
    {
        _overlay.Clear();
        ShowMode("已清屏", ModeLevel.Hint);
        RefreshDiagnostics();
    }

    // ================= 模式指示（实时判定）=================

    private enum ModeLevel
    {
        Hint,
        Scripture,
        FreeText,
        Warning,
        Error,
    }

    private void ShowMode(string text, ModeLevel level)
    {
        ModeText.Text = text;
        ModeText.Foreground = level switch
        {
            ModeLevel.Scripture => Brushes.DarkGreen,
            ModeLevel.FreeText => Brushes.MediumBlue,
            ModeLevel.Warning => Brushes.DarkOrange,
            ModeLevel.Error => Brushes.Firebrick,
            _ => Brushes.Gray,
        };
    }

    /// <summary>
    /// M3 验收：输入 <c>约3:16</c> 显示「经文」，输入 <c>欢迎新朋友</c> 显示「自由文本」。
    /// </summary>
    private void RefreshMode()
    {
        string input = InputBox.Text;

        if (IsComposing)
        {
            // 组合中不做判定也不报错：半成品必然解析失败，此时刷出「未知书卷」是噪音。
            ShowMode("输入法组合中…", ModeLevel.Hint);
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            ShowMode("输入经文引用（约3:16 / 罗8:28 / 诗23:1-6）或任意文字", ModeLevel.Hint);
            return;
        }

        if (_parser is null || _repository is null)
        {
            ShowMode("自由文本 → 原样上屏（经文库不可用）", ModeLevel.Warning);
            return;
        }

        if (_parser.TryParse(input, out VerseRef? reference, out string? error))
        {
            IReadOnlyList<VerseText> verses = _repository.Lookup(reference);

            if (verses.Count == 0)
            {
                ShowMode("✗ 该节在库中查不到文本", ModeLevel.Error);
                return;
            }

            string label = verses[0].Label;
            string pages = verses.Count > 1 ? $"，{verses.Count} 页" : string.Empty;

            ShowMode($"经文 → {label}{pages}", ModeLevel.Scripture);
            return;
        }

        if (error is not null)
        {
            ShowMode("✗ " + error, ModeLevel.Error);
            return;
        }

        ShowMode("自由文本 → 原样上屏", ModeLevel.FreeText);
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
            RefreshScreens();
            RefreshDiagnostics();

            TargetScreenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// P0-13：显示器配置变更后由 <c>App</c> 调用，刷新屏幕列表与状态。
    /// 叠加层的重新定位由 <c>App</c> 负责，这里只管界面。
    /// </summary>
    public void NotifyScreensChanged()
    {
        RefreshScreens();
        RefreshDiagnostics();
        ShowMode($"显示器配置已变更，当前 {System.Windows.Forms.Screen.AllScreens.Length} 块屏", ModeLevel.Warning);
    }

    /// <summary>
    /// M5 验收：强制抛异常，程序继续运行，日志有记录，副屏无变化。
    /// </summary>
    /// <remarks>
    /// 直接在事件处理器里抛：异常会走到 <c>App.DispatcherUnhandledException</c>，
    /// 那里写日志并把 <c>Handled</c> 置真，进程继续。**副屏不应有任何变化**——
    /// 这正是「叠加层与控制窗解耦」要保证的事。
    /// </remarks>
    private void OnForceException(object sender, RoutedEventArgs e)
        => throw new InvalidOperationException("这是 M5 验收用的人为异常，用来验证全局异常捕获。");

    // ================= 压力测试（M2 验收）=================

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
            DisplayContent content = ContentBuilder.FromFreeText("测试");

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

    // ================= 状态与自检 =================

    private void OnFadeMeasured(object? sender, FadeMeasurement m)
    {
        _lastFade = m;
        _hasFadeSample = true;
    }

    private void OnOverlayContentChanged(object? sender, EventArgs e)
    {
        PreviewEmptyHint.Visibility = _overlay.IsContentVisible
            ? Visibility.Collapsed
            : Visibility.Visible;

        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        DisplayContent? content = _overlay.CurrentContent;

        StatusScreen.Text = "副屏：" + _overlay.TargetScreenDeviceName;

        StatusPage.Text = content is null
            ? "页：—"
            : content.HasMultiplePages
                ? $"页：{content.Index + 1}/{content.PageCount}"
                : "页：单页";

        StatusDatabase.Text = _repository is null
            ? "库：不可用"
            : $"库：CUV v{_databaseVersion ?? "?"}";

        StatusIme.Text = IsComposing ? "输入法：组合中" : "输入法：待机";
        StatusHotkeys.Text = HotkeyStatus;
    }

    private void RefreshDiagnostics()
    {
        RefreshStatusBar();

        PreviewEmptyHint.Visibility = _overlay.IsContentVisible
            ? Visibility.Collapsed
            : Visibility.Visible;

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
            "当前内容 {0}   页 {1}   正文字号 {2:F1}px（上限 {3:F0}）",
            content is null ? "(空)" : content.Kind.ToString(),
            content is null ? "-" : $"{content.Index + 1}/{content.PageCount}",
            _overlay.CurrentBodyFontSize,
            _config.Typography.MaxFontSize);

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
