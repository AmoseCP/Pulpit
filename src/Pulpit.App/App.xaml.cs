using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Pulpit.App.Diagnostics;
using Pulpit.App.Views;

namespace Pulpit.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private ControlWindow? _control;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 叠加层从不 Close（L4），控制窗口关掉才算退出，所以不能用默认的
        // OnLastWindowClose——那会让「叠加层还在」变成永不退出。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        InstallGlobalExceptionHandlers();

        base.OnStartup(e);

        AppLog.Info("Pulpit M0 尖刺启动。");

        _overlay = new OverlayWindow();

        // ShowActivated=False 已在 XAML 声明；Show() 不会夺取焦点。
        _overlay.Show();

        _control = new ControlWindow(_overlay);
        _control.Closed += OnControlClosed;
        _control.Show();
    }

    private void OnControlClosed(object? sender, EventArgs e)
    {
        AppLog.Info("控制窗口关闭，进程退出。");

        // 只有此刻才允许叠加层真正关闭（L4）。
        _overlay?.AllowCloseOnShutdown();
        _overlay?.Close();

        Shutdown();
    }

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
