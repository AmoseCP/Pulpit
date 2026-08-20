using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Pulpit.App.Data;
using Pulpit.App.Diagnostics;
using Pulpit.App.Interop;
using Pulpit.App.Views;
using Pulpit.Core.Config;
using Pulpit.Core.Content;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;

namespace Pulpit.App;

public partial class App : System.Windows.Application
{
    private readonly ConfigStore _configStore = new();

    private AppConfig _config = new();
    private SingleInstanceGuard? _singleInstance;
    private OverlayWindow? _overlay;
    private BadgeWindow? _badge;
    private ControlWindow? _control;
    private BibleRepository? _repository;
    private GlobalHotkeyService? _hotkeys;
    private DispatcherTimer? _displayDebounce;
    private bool _displayHooked;

    /// <summary>
    /// 启动是否已走完。「吞掉异常继续跑」的直播原则只适用于启动**之后**——
    /// 启动半途的异常被吞掉的结果是一个看不见的僵尸进程：没有任何窗口，却握着
    /// 单实例互斥锁，之后每次重启都被「已在运行」拒绝（真机首启实测撞上过）。
    /// </summary>
    private bool _startupComplete;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 叠加层从不 Close（L4），控制窗口关掉才算退出，所以不能用默认的
        // OnLastWindowClose——那会让「叠加层还在」变成永不退出。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        InstallGlobalExceptionHandlers();

        base.OnStartup(e);

        // L15 / P0-14：两份实例会争抢全局热键，第二份注册会静默失败。
        if (!SingleInstanceGuard.TryAcquire(out _singleInstance))
        {
            AppLog.Warn("已有一份 Pulpit 在运行，本次启动被拒绝。");

            // 这是启动期的**主动**提示，不是未处理异常对话框——P0-14 要求有提示。
            MessageBox.Show(
                "Pulpit 已经在运行了。\n\n两份实例会争抢全局热键（F7–F12），"
                + "第二份的注册会静默失败，按键就没反应了。\n请使用已经打开的那一个窗口。",
                "Pulpit 已在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        AppLog.Info("Pulpit 启动。");

        try
        {
            StartUp();
            _startupComplete = true;
        }
        catch (Exception ex)
        {
            // 启动期 fail fast。这是主动的启动失败提示（同 P0-14 的「已在运行」框），
            // 不是直播中的未处理异常对话框——此刻还没有直播可保护。
            AppLog.Error("启动失败，进程退出。", ex);
            MessageBox.Show(
                "Pulpit 启动失败：\n\n" + ex.Message + "\n\n详情见日志：" + AppLog.CurrentLogPath,
                "Pulpit 无法启动",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _singleInstance?.Dispose();
            Shutdown(1);
        }
    }

    private void StartUp()
    {
        _config = _configStore.Load(out IReadOnlyList<string> notes);
        foreach (string note in notes)
        {
            AppLog.Warn("配置：" + note);
        }

        AppLog.Info($"配置文件：{_configStore.FilePath}");

        _repository = OpenRepository(out string? databaseError);
        string? databaseVersion = _repository?.SchemaVersion;

        // 解析 + 查询 + 三态判定全在 Core 的 ContentComposer 里；两个参数都可为 null，
        // 经文库打不开时它会让一切输入走自由文本（该降级路径有单测盯着）。
        var composer = new ContentComposer(
            _repository,
            _repository is null ? null : new ReferenceParser(_repository));

        // P2-1 关键词反查。索引是懒建的（首次搜索时才花那约 52ms），
        // 所以这里 new 出来不产生任何启动开销。
        VerseSearchIndex? searchIndex =
            _repository is null ? null : new VerseSearchIndex(_repository);

        _overlay = new OverlayWindow(_config);

        // ShowActivated=False 已在 XAML 声明；Show() 不会夺取焦点。
        _overlay.Show();

        // P2-4 副屏第二区域。独立窗口，与正文带子互不影响。
        // 先 Show 再 ApplyConfig：ApplyConfig 会按配置决定淡入还是保持透明，
        // 而它需要窗口已经有 HWND（定位走 SetWindowPos）。
        _badge = new BadgeWindow(_config);
        _badge.Show();
        _badge.ApplyConfig(_config);

        _control = new ControlWindow(
            _overlay, composer, searchIndex, _config, databaseVersion, databaseError);
        _control.TargetScreenChanged += OnTargetScreenChanged;
        _control.TextModeChanged += OnTextModeChanged;
        _control.AppearanceChanged += OnAppearanceChanged;
        _control.AppearanceSaveRequested += OnAppearanceSaveRequested;
        _control.Closed += OnControlClosed;
        _control.Show();

        RegisterHotkeys(_config);
        HookDisplayChanges();
    }

    // ================= 经文库 =================

    /// <summary>
    /// 打开经文库。失败**不阻止启动**——叠加层与自由文本仍然可用，
    /// 错误信息交给控制窗口显示（副屏上绝不出现错误信息）。
    /// </summary>
    private static BibleRepository? OpenRepository(out string? error)
    {
        // M6：随包嵌入 + 首次运行解出到 %LOCALAPPDATA%\Pulpit\。
        string? path = DatabaseProvisioner.EnsureLocalCopy(out error);

        if (path is null)
        {
            AppLog.Error("经文库无法就位，经文查询不可用（自由文本仍可用）。" + error);
            return null;
        }

        try
        {
            var repository = new BibleRepository(path);
            AppLog.Info($"经文库已打开：{path}（schema_version={repository.SchemaVersion}）");
            return repository;
        }
        catch (BibleDatabaseException ex)
        {
            error = ex.Message;
            AppLog.Error("经文库打开失败，经文查询不可用（自由文本仍可用）。", ex);
            return null;
        }
    }

    // ================= 热键 =================

    /// <summary>
    /// 注册全局热键。注册失败**不阻止启动**——按钮仍然可用，
    /// 失败的键位在状态栏点名告警（M4 验收）。
    /// </summary>
    private void RegisterHotkeys(AppConfig config)
    {
        _hotkeys = new GlobalHotkeyService();
        _hotkeys.Pressed += OnHotkeyPressed;

        HotkeyRegistrationResult result = _hotkeys.Register(config.Hotkeys);

        if (_control is not null)
        {
            _control.HotkeyStatus = result.StatusText;
        }

        if (!result.AllSucceeded)
        {
            AppLog.Warn("部分热键注册失败：" + string.Join(" ", result.Failed));
        }
    }

    /// <summary>
    /// 热键分派。全部走控制窗口的公开方法，与鼠标点按钮走同一条路径——
    /// 两条入口共用一套逻辑，才不会出现「按钮能用热键不能用」这种分叉。
    /// </summary>
    private void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        if (_control is null)
        {
            return;
        }

        switch (action)
        {
            case HotkeyAction.SendZh:
                _control.SendCurrentInput();
                break;

            case HotkeyAction.SendEn:
                _control.SendEnglish();
                break;

            case HotkeyAction.PrevPage:
                _control.PrevPage();
                break;

            case HotkeyAction.NextPage:
                _control.NextPage();
                break;

            case HotkeyAction.Clear:
                _control.Clear();
                break;

            default:
                AppLog.Warn($"收到未知热键动作 {action}，忽略。");
                break;
        }
    }

    // ================= 显示器变更（P0-13）=================

    private void HookDisplayChanges()
    {
        _displayDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            // 拔插 HDMI 会连发好几次事件，且系统那边的屏幕枚举要过一会儿才稳定。
            Interval = TimeSpan.FromMilliseconds(700),
        };

        _displayDebounce.Tick += OnDisplayDebounceTick;

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _displayHooked = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents 在它自己的线程上回调，必须切回 UI 线程才能碰窗口。
        _ = Dispatcher.InvokeAsync(() =>
        {
            _displayDebounce?.Stop();
            _displayDebounce?.Start();
        });
    }

    private void OnDisplayDebounceTick(object? sender, EventArgs e)
    {
        _displayDebounce?.Stop();

        int count = System.Windows.Forms.Screen.AllScreens.Length;
        AppLog.Info($"检测到显示器配置变更，当前 {count} 块屏，重新定位叠加层。");

        // 目标屏不在了会退回主屏而不是崩（ResolveTargetScreen 内部处理）。
        _overlay?.Reposition();
        _badge?.Reposition();
        _control?.NotifyScreensChanged();
    }

    // ================= 配置持久化（P0-12）=================

    private void OnTargetScreenChanged(object? sender, EventArgs e)
    {
        if (_overlay is null)
        {
            return;
        }

        _config = _config with { TargetScreenDeviceName = _overlay.TargetScreenDeviceName };

        // 角标跟着正文一起换屏——它们本来就该在同一块副屏上。
        _badge?.ApplyConfig(_config);

        Persist($"目标屏 {_config.TargetScreenDeviceName}");
    }

    /// <summary>P1-4：记住原文/清洗版的选择。</summary>
    private void OnTextModeChanged(object? sender, EventArgs e)
    {
        if (_control is null)
        {
            return;
        }

        _config = _config with { Text = new TextConfig { UseRawText = _control.UseRawText } };
        Persist($"正文来源 useRawText={_control.UseRawText}");
    }

    /// <summary>P1-3：外观实时应用，**不落盘**。</summary>
    private void OnAppearanceChanged(object? sender, AppConfig config)
    {
        _config = config;
        _overlay?.ApplyConfig(config);
        _badge?.ApplyConfig(config);
    }

    /// <summary>P1-3：操作员点了「保存为默认」才落盘。</summary>
    private void OnAppearanceSaveRequested(object? sender, EventArgs e) => Persist("外观设置");

    private void Persist(string what)
    {
        if (_configStore.TrySave(_config, out string? error))
        {
            AppLog.Info($"已记住：{what}");
        }
        else
        {
            // 写不进去不是停机理由——只是下次启动记不住。
            AppLog.Warn($"{what} 写入配置失败（下次启动会记不住）：{error}");
        }
    }

    // ================= 退出 =================

    private void OnControlClosed(object? sender, EventArgs e)
    {
        AppLog.Info("控制窗口关闭，进程退出。");

        try
        {
            if (_displayHooked)
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _displayHooked = false;
            }

            if (_displayDebounce is not null)
            {
                _displayDebounce.Stop();
                _displayDebounce.Tick -= OnDisplayDebounceTick;
                _displayDebounce = null;
            }

            // 热键先注销：留着不放会让下一次启动注册失败。
            _hotkeys?.Dispose();

            // 只有此刻才允许两个叠加窗口真正关闭（L4）。
            _overlay?.AllowCloseOnShutdown();
            _overlay?.Close();

            _badge?.AllowCloseOnShutdown();
            _badge?.Close();

            _repository?.Dispose();
            _singleInstance?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("退出清理时出错（仍将退出）。", ex);
        }
        finally
        {
            // Shutdown 必须无条件到达：清理半途抛异常若被全局处理器吞掉，
            // OnExplicitShutdown 会让进程带着置顶叠加窗口永远活着。
            Shutdown();
        }
    }

    // ================= 全局异常 =================

    /// <summary>
    /// CLAUDE.md「绝不允许的行为」第一条：直播中弹未处理异常对话框 = 事故。
    /// 三条通道全部捕获 → 写日志 → 继续运行。副屏内容保持原状，不做任何提示。
    /// </summary>
    private void InstallGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!_startupComplete)
        {
            // 启动尚未完成：吞掉并继续只会留下一个无窗口、握着单实例锁的僵尸。
            // 记录、明示、退出（同 OnStartup 的 fail-fast 路径，这里兜住异步到达的）。
            AppLog.Error("启动尚未完成即遇到未处理异常，进程退出。", e.Exception);
            e.Handled = true;
            _singleInstance?.Dispose();
            Shutdown(1);
            return;
        }

        AppLog.Error("UI 线程未处理异常（已吞掉，程序继续运行）。", e.Exception);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 这一条无法阻止 CLR 终止进程，但至少要留下现场记录。
        AppLog.Error("AppDomain 未处理异常。", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Task 未观察异常（已标记为已观察）。", e.Exception);
        e.SetObserved();
    }
}
