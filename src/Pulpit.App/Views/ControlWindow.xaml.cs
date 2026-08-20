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
    private readonly ContentComposer _composer;
    private readonly VerseSearchIndex? _searchIndex;
    private readonly SendHistory _history = new();
    private AppConfig _config;
    private readonly string? _databaseVersion;
    private readonly DispatcherTimer _poll;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly long _baselineWorkingSet;

    private FadeMeasurement _lastFade;
    private bool _hasFadeSample;
    private bool _useRawText;
    private string? _lastSentInput;

    /// <summary>
    /// 初始化外观控件时抑制回调，否则一赋值就当成操作员改动。
    /// 初值必须为 <c>true</c>：XAML 解析阶段（InitializeComponent 内部）给 Slider 设
    /// Min/Max/Value 就会触发 ValueChanged，那一刻声明得比它晚的控件还是 null——
    /// 真机首次启动就在这里 NRE，窗口永远出不来。闸门由构造函数里的
    /// LoadAppearanceControls() 灌完初值后在 finally 中打开。
    /// </summary>
    private bool _loadingAppearance = true;

    public ControlWindow(
        OverlayWindow overlay,
        ContentComposer composer,
        VerseSearchIndex? searchIndex,
        AppConfig config,
        string? databaseVersion,
        string? databaseError)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _searchIndex = searchIndex;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _databaseVersion = databaseVersion;
        _useRawText = config.Text.UseRawText;

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

        RawTextToggle.IsChecked = _useRawText;
        RefreshHistory();
        LoadAppearanceControls();

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

    /// <summary>操作员切换了原文/清洗版。<c>App</c> 据此写 <c>text.useRawText</c>（P1-4）。</summary>
    public event EventHandler? TextModeChanged;

    /// <summary>
    /// 外观被改动（P1-3）。<c>App</c> 据此调 <c>OverlayWindow.ApplyConfig</c>，**不落盘**。
    /// </summary>
    public event EventHandler<AppConfig>? AppearanceChanged;

    /// <summary>操作员点了「保存为默认」。<c>App</c> 据此落盘。</summary>
    public event EventHandler? AppearanceSaveRequested;

    /// <summary>当前是否显示原文（<c>text_raw</c>）。</summary>
    public bool UseRawText => _useRawText;

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
        // handledEventsToo 必须为 true：TextBox 的编辑器自己消费这些文本输入事件并
        // 标记为已处理，普通 AddHandler 一个都收不到——那样 IsComposing 恒为 false，
        // F9 在拼音组合中到达时会把半截未确认的文字直接送上副屏（L8 的事故原型）。
        InputBox.AddHandler(
            TextCompositionManager.TextInputStartEvent,
            new TextCompositionEventHandler(OnCompositionStart),
            handledEventsToo: true);

        InputBox.AddHandler(
            TextCompositionManager.TextInputUpdateEvent,
            new TextCompositionEventHandler(OnCompositionUpdate),
            handledEventsToo: true);

        InputBox.AddHandler(
            TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler(OnCompositionEnd),
            handledEventsToo: true);

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
        ComposeResult result = _composer.Compose(input, _useRawText);

        if (result.HasError)
        {
            // P0-10：报错只出现在控制窗口，副屏保持原状，绝不上副屏。
            ShowMode("✗ " + result.Error, ModeLevel.Error);
            AppLog.Info($"投放被拒：{input} → {result.Error}");
            return;
        }

        if (result.IsEmpty)
        {
            ShowMode("没有可投放的内容", ModeLevel.Hint);
            return;
        }

        DisplayContent content = result.Content!;

        _lastSentInput = input;
        _overlay.Show(content);
        AppLog.Info($"投放：{input}（{content.Kind}，{content.PageCount} 页）");

        if (_history.Record(input, content))
        {
            RefreshHistory();
        }

        RefreshDiagnostics();
    }

    // ================= 歌词模式（P2-2）=================

    /// <summary>
    /// 投放多行歌词。空行即分页点，超长小节按 <c>lyrics.linesPerPage</c> 切开。
    /// </summary>
    /// <remarks>
    /// 歌词框的 <c>AcceptsReturn=True</c> 不违反 L8：Enter 只在框内插换行，
    /// 永远不会送出——全窗口没有任何 <c>IsDefault</c> 按钮。投放走这个按钮，
    /// 投放之后 F7/F8/F12 照常可用。
    /// </remarks>
    private void OnSendLyrics(object sender, RoutedEventArgs e)
    {
        DisplayContent content = ContentBuilder.FromLyrics(
            LyricsBox.Text, _config.Lyrics.LinesPerPage);

        if (content.IsEmpty)
        {
            LyricsHint.Text = "歌词框是空的";
            return;
        }

        // 歌词不经过 ContentComposer，「原文/清洗版切换后原地重投」那条路径对它不适用；
        // 置空 _lastSentInput，免得切换开关时把歌词当引用去重新解析。
        _lastSentInput = null;

        _overlay.Show(content);

        LyricsHint.Text = $"已投放，共 {content.PageCount} 页——用 F8 / F7 翻页";
        AppLog.Info($"投放歌词：{content.PageCount} 页。");

        RefreshDiagnostics();
    }

    private void OnClearLyrics(object sender, RoutedEventArgs e)
    {
        LyricsBox.Clear();
        LyricsHint.Text = string.Empty;
    }

    /// <summary>边打边告诉操作员会分成几页，省得投出去才发现分页不对。</summary>
    private void OnLyricsChanged(object sender, TextChangedEventArgs e)
    {
        DisplayContent preview = ContentBuilder.FromLyrics(
            LyricsBox.Text, _config.Lyrics.LinesPerPage);

        LyricsHint.Text = preview.IsEmpty
            ? string.Empty
            : $"将分成 {preview.PageCount} 页（空行处分页，每页最多 {_config.Lyrics.LinesPerPage} 行）";
    }

    // ================= 关键词反查（P2-1）=================

    /// <summary>
    /// 用输入框里的内容反查出处。
    /// </summary>
    /// <remarks>
    /// 刻意复用同一个输入框而不是另开一个搜索框：输入框已经会告诉操作员「这是自由文本」，
    /// 此时旁边那个「反查出处」按钮正是他需要的下一步。两个输入框只会让人分不清该敲哪个。
    /// <para>首次搜索要建索引（实测约 52ms，31021 行）。同步做即可——
    /// 50ms 的停顿感知不到，而为它引一层后台线程要处理并发建索引，不值得。</para>
    /// </remarks>
    private void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_searchIndex is null)
        {
            ShowMode("经文库不可用，无法反查", ModeLevel.Warning);
            return;
        }

        if (IsComposing)
        {
            ShowMode("输入法正在组合候选词，请先确认", ModeLevel.Warning);
            return;
        }

        SearchResult result = _searchIndex.Search(InputBox.Text);

        SearchList.ItemsSource = new List<SearchHit>(result.Hits);

        if (result.Hits.Count == 0)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            ShowMode(result.Notice ?? "反查没有结果", ModeLevel.Warning);
            return;
        }

        SearchPanel.Visibility = Visibility.Visible;
        SearchList.SelectedIndex = 0;

        SearchHint.Text = result.Truncated
            ? $"共 {result.TotalMatches} 处，只列前 {result.Hits.Count} 条——把关键词写长一点能缩小范围"
            : $"共 {result.Hits.Count} 处。双击一条即投放，或「填入选中」只填不投。";

        AppLog.Info($"反查「{InputBox.Text}」→ {result.TotalMatches} 处。");
    }

    /// <summary>双击结果 = 填入并投放。与历史一致：单击只选中，不上屏。</summary>
    private void OnSearchActivate(object sender, MouseButtonEventArgs e)
    {
        if (FillFromSearch() is string input)
        {
            Send(input);
        }
    }

    /// <summary>只填入输入框，不投放——操作员可能想改成范围再投。</summary>
    private void OnSearchFill(object sender, RoutedEventArgs e) => FillFromSearch();

    private string? FillFromSearch()
    {
        if (SearchList.SelectedItem is not SearchHit hit)
        {
            ShowMode("先在反查结果里选一条", ModeLevel.Hint);
            return null;
        }

        // InputForm 是用 books.short_zh 拼的，已核对 66 个短称全部能被解析器解析回原书卷。
        InputBox.Text = hit.InputForm;
        return hit.InputForm;
    }

    private void OnSearchClose(object sender, RoutedEventArgs e)
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchList.ItemsSource = null;
    }

    // ================= 历史（P1-2）=================

    /// <summary>
    /// 复投走**双击**，而不是单击。
    /// </summary>
    /// <remarks>
    /// 单击即投在直播中太危险：操作员想选中看一眼就会把内容甩上副屏。
    /// 双击 + 「复投选中」按钮两条路都通，误触的代价却降到零。
    /// </remarks>
    private void OnHistoryActivate(object sender, MouseButtonEventArgs e) => ReplaySelectedHistory();

    private void OnHistoryReplay(object sender, RoutedEventArgs e) => ReplaySelectedHistory();

    private void ReplaySelectedHistory()
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry)
        {
            ShowMode("先在历史里选一条", ModeLevel.Hint);
            return;
        }

        // 把输入框也填上：操作员接着可能想改个节号再投。
        InputBox.Text = entry.Input;

        // 重新走一遍 Compose，所以复投遵循**当前**的原文/清洗版设置（P1-4），
        // 而不是投出一份带着旧设置的快照。
        Send(entry.Input);
    }

    private void OnHistoryClear(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        RefreshHistory();
        AppLog.Info("历史已清空。");
    }

    private void RefreshHistory()
    {
        // Entries 是同一个列表对象，直接赋值 ItemsSource 不会触发刷新，
        // 所以每次拷一份新列表进去。历史最多 30 条，拷贝成本可以忽略。
        HistoryList.ItemsSource = new List<HistoryEntry>(_history.Entries);

        HistoryHint.Text = _history.Count == 0
            ? "还没投过经文"
            : $"{_history.Count} 条（最多 {_history.Capacity}）";
    }

    /// <summary>
    /// P1-4：原文 ↔ 清洗版切换。
    /// </summary>
    /// <remarks>
    /// 切换后**原地重投并保持当前页**。跳回第 1 页会让操作员在多页经文中途切换时
    /// 丢失位置——现场重新按 F8 翻回去是可见的抖动。
    /// </remarks>
    private void OnToggleRawText(object sender, RoutedEventArgs e)
    {
        _useRawText = RawTextToggle.IsChecked == true;

        AppLog.Info($"正文来源切换为 {(_useRawText ? "text_raw（原文）" : "text_display（清洗版）")}。");

        RefreshMode();
        RefreshStatusBar();

        TextModeChanged?.Invoke(this, EventArgs.Empty);

        ReprojectPreservingPage();
    }

    /// <summary>用当前设置重投上一次的输入，并把页码停在原处。</summary>
    private void ReprojectPreservingPage()
    {
        if (_lastSentInput is null || !_overlay.IsContentVisible)
        {
            return;
        }

        int page = _overlay.CurrentContent?.Index ?? 0;

        ComposeResult result = _composer.Compose(_lastSentInput, _useRawText);

        if (!result.HasContent)
        {
            return;
        }

        DisplayContent content = result.Content!;

        // 页数理论上不会因为换正文来源而变（分页按并节组，与文本内容无关），
        // 但仍然夹一下：万一变了，宁可停在末页也不要越界。
        content.Index = Math.Min(page, Math.Max(0, content.PageCount - 1));

        _overlay.Show(content);
        RefreshDiagnostics();
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
    /// <remarks>
    /// 每次按键都走一遍 <see cref="ContentComposer"/>——判定逻辑与真正投放时**完全同一条路径**，
    /// 所以模式指示上写什么，按 F9 就一定得到什么。两套判定迟早会分叉。
    /// </remarks>
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
            ShowMode("输入经文引用（约3:16 / 诗23:1-6 / 约3:16;罗8:28）或任意文字", ModeLevel.Hint);
            return;
        }

        if (!_composer.ScriptureAvailable)
        {
            ShowMode("自由文本 → 原样上屏（经文库不可用）", ModeLevel.Warning);
            return;
        }

        ComposeResult result = _composer.Compose(input, _useRawText);

        if (result.HasError)
        {
            ShowMode("✗ " + result.Error, ModeLevel.Error);
            return;
        }

        if (result.IsEmpty)
        {
            ShowMode("输入经文引用或任意文字", ModeLevel.Hint);
            return;
        }

        DisplayContent content = result.Content!;

        if (content.Kind == ContentKind.FreeText)
        {
            ShowMode("自由文本 → 原样上屏", ModeLevel.FreeText);
            return;
        }

        // 连续引用时把每一处都列出来：约翰福音 3:16 + 罗马书 8:28
        string label = string.Join(" + ", content.SourceLabels);
        string pages = content.HasMultiplePages ? $"，{content.PageCount} 页" : string.Empty;

        ShowMode($"经文 → {label}{pages}", ModeLevel.Scripture);
    }

    // ================= 外观（P1-3）=================

    /// <summary>
    /// 把当前配置灌进外观控件。
    /// </summary>
    /// <remarks>
    /// <c>_loadingAppearance</c> 是必须的：给 Slider 赋值会触发 ValueChanged，
    /// 没有这个闸门的话，窗口一打开就会被当成「操作员改了外观」而回写配置。
    /// </remarks>
    private void LoadAppearanceControls()
    {
        _loadingAppearance = true;

        try
        {
            MaxFontSlider.Value = _config.Typography.MaxFontSize;
            OpacitySlider.Value = _config.Band.BackgroundOpacity;
            HeightSlider.Value = _config.Band.HeightPercent;
            FadeSlider.Value = _config.Animation.FadeMs;

            bool top = string.Equals(
                _config.Band.VerticalAnchor, "top", StringComparison.OrdinalIgnoreCase);
            bool center = string.Equals(
                _config.Band.VerticalAnchor, "center", StringComparison.OrdinalIgnoreCase);
            bool full = string.Equals(
                _config.Band.VerticalAnchor, "fullscreen", StringComparison.OrdinalIgnoreCase);

            AnchorTop.IsChecked = top;
            AnchorCenter.IsChecked = center;
            AnchorFull.IsChecked = full;
            AnchorBottom.IsChecked = !top && !center && !full;

            LoadFontList();

            BadgeEnabled.IsChecked = _config.Badge.Enabled;
            BadgeTextBox.Text = _config.Badge.Text;
            SelectBadgeCorner(_config.Badge.Corner);

            RefreshAppearanceLabels();
        }
        finally
        {
            _loadingAppearance = false;
        }
    }

    private void LoadFontList()
    {
        if (FontList.Items.Count == 0)
        {
            var families = new List<string>();

            foreach (System.Windows.Media.FontFamily family in System.Windows.Media.Fonts.SystemFontFamilies)
            {
                families.Add(family.Source);
            }

            families.Sort(StringComparer.CurrentCulture);

            // 配置里的字体可能没装在这台机器上——也要列出来，否则一打开面板
            // 选中项就被悄悄换成别的字体了。
            if (!families.Contains(_config.Typography.FontFamily))
            {
                families.Insert(0, _config.Typography.FontFamily);
            }

            FontList.ItemsSource = families;
        }

        FontList.SelectedItem = _config.Typography.FontFamily;
    }

    private void SelectBadgeCorner(string corner)
    {
        foreach (object item in BadgeCorner.Items)
        {
            if (item is ComboBoxItem { Tag: string tag }
                && string.Equals(tag, corner, StringComparison.OrdinalIgnoreCase))
            {
                BadgeCorner.SelectedItem = item;
                return;
            }
        }

        BadgeCorner.SelectedIndex = 0;
    }

    private string CurrentBadgeCorner() =>
        BadgeCorner.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "topRight";

    /// <summary>与字体下拉同理：给它一个精确签名的处理器，不靠委托逆变。</summary>
    private void OnBadgeCornerChanged(object sender, SelectionChangedEventArgs e) => ApplyAppearance();

    private void OnBadgeTextChanged(object sender, TextChangedEventArgs e) => ApplyAppearance();

    private void RefreshAppearanceLabels()
    {
        MaxFontValue.Text = string.Format(CultureInfo.InvariantCulture, "{0:F0} px", MaxFontSlider.Value);
        OpacityValue.Text = string.Format(CultureInfo.InvariantCulture, "{0:P0}", OpacitySlider.Value);
        HeightValue.Text = string.Format(CultureInfo.InvariantCulture, "{0:P0}", HeightSlider.Value);

        FadeValue.Text = FadeSlider.Value < 1
            ? "直切"
            : string.Format(CultureInfo.InvariantCulture, "{0:F0} ms", FadeSlider.Value);
    }

    private void OnAppearanceValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyAppearance();

    private void OnAppearanceToggled(object sender, RoutedEventArgs e) => ApplyAppearance();

    /// <summary>
    /// 字体下拉。刻意给它一个**精确签名**的处理器：<c>SelectionChanged</c> 的委托是
    /// <c>(object, SelectionChangedEventArgs)</c>，复用 <c>RoutedEventArgs</c> 版本要靠
    /// 委托逆变，能不能过 XAML 标记编译器不值得赌。
    /// </summary>
    private void OnFontChanged(object sender, SelectionChangedEventArgs e) => ApplyAppearance();

    /// <summary>
    /// 把控件上的值组装成新配置并实时应用。**不落盘**——落盘要操作员点「保存为默认」。
    /// </summary>
    /// <remarks>
    /// 滑块拖动时 ValueChanged 每帧都触发，每次都写一遍 config.json 是没必要的磁盘噪音；
    /// 而且彩排时来回试参数，不该每一步都改掉默认值。
    /// </remarks>
    private void ApplyAppearance()
    {
        if (_loadingAppearance)
        {
            return;
        }

        RefreshAppearanceLabels();

        string fontFamily = FontList.SelectedItem as string ?? _config.Typography.FontFamily;

        AppConfig candidate = _config with
        {
            Band = _config.Band with
            {
                HeightPercent = HeightSlider.Value,
                BackgroundOpacity = OpacitySlider.Value,
                VerticalAnchor = AnchorTop.IsChecked == true ? "top"
                    : AnchorCenter.IsChecked == true ? "center"
                    : AnchorFull.IsChecked == true ? "fullscreen"
                    : "bottom",
            },
            Typography = _config.Typography with
            {
                MaxFontSize = MaxFontSlider.Value,
                FontFamily = fontFamily,
            },
            Animation = _config.Animation with
            {
                FadeMs = (int)Math.Round(FadeSlider.Value),
            },
            Badge = _config.Badge with
            {
                Enabled = BadgeEnabled.IsChecked == true,
                Text = BadgeTextBox.Text,
                Corner = CurrentBadgeCorner(),
            },
        };

        // Sanitize 是免费的保险：滑块范围本就在合法区间内，但万一以后改了范围，
        // 这里会把越界值夹回来而不是让副屏出怪样子。
        _config = candidate.Sanitize(out IReadOnlyList<string> corrections);

        foreach (string note in corrections)
        {
            AppLog.Warn("外观设置被修正：" + note);
        }

        AppearanceChanged?.Invoke(this, _config);

        AppearanceHint.Text = "已实时应用；点「保存为默认」才会记住";
    }

    private void OnSaveAppearance(object sender, RoutedEventArgs e)
    {
        AppearanceSaveRequested?.Invoke(this, EventArgs.Empty);
        AppearanceHint.Text = "已保存为默认";
        AppLog.Info("外观设置已保存为默认。");
    }

    /// <summary>恢复到内置默认值（只重置外观相关字段，不动目标屏与正文来源）。</summary>
    private void OnResetAppearance(object sender, RoutedEventArgs e)
    {
        var defaults = new AppConfig();

        _config = _config with
        {
            Band = defaults.Band,
            Typography = defaults.Typography,
            Animation = defaults.Animation,

            // 角标只重置几何与显隐，**保留文字**——那是操作员敲进去的内容，不是外观参数。
            Badge = defaults.Badge with { Text = _config.Badge.Text },
        };

        LoadAppearanceControls();

        AppearanceChanged?.Invoke(this, _config);

        AppearanceHint.Text = "已恢复出厂设置；点「保存为默认」才会记住";
        AppLog.Info("外观设置已恢复出厂值。");
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

        StatusDatabase.Text = !_composer.ScriptureAvailable
            ? "库：不可用"
            : $"库：CUV v{_databaseVersion ?? "?"}{(_useRawText ? "（原文）" : string.Empty)}";

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
