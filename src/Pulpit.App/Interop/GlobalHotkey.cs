using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Pulpit.App.Diagnostics;
using Pulpit.Core.Config;

namespace Pulpit.App.Interop;

/// <summary>操作员可触发的动作。</summary>
public enum HotkeyAction
{
    /// <summary>F9：送出中文经文。</summary>
    SendZh,

    /// <summary>F10：送出英文经文（v1 只提示未安装，P0-9）。</summary>
    SendEn,

    /// <summary>F7：上一页。</summary>
    PrevPage,

    /// <summary>F8：下一页。</summary>
    NextPage,

    /// <summary>F12：清屏。</summary>
    Clear,
}

/// <summary>一次注册的结果。</summary>
public sealed record HotkeyRegistrationResult(
    IReadOnlyList<string> Registered,
    IReadOnlyList<string> Failed)
{
    public bool AllSucceeded => Failed.Count == 0;

    /// <summary>
    /// 状态栏文本。M4 验收要求「注册失败（键位被占）时在状态栏明确告警，列出失败的键」——
    /// 失败必须点名到具体键位，「热键注册失败」这种笼统说法排查不了。
    /// </summary>
    public string StatusText => AllSucceeded
        ? $"热键：{string.Join(" ", Registered)} 已就绪"
        : $"⚠ 热键 {string.Join(" ", Failed)} 注册失败（被其他程序占用），可用：{(Registered.Count == 0 ? "无" : string.Join(" ", Registered))}";
}

/// <summary>
/// 全局热键。
/// </summary>
/// <remarks>
/// <para><b>为什么挂在自己的隐藏窗口上</b>：<c>RegisterHotKey</c> 要一个窗口句柄收
/// <c>WM_HOTKEY</c>。挂在控制窗口上意味着控制窗口一旦被关闭或重建，热键就跟着失效；
/// 自建一个 0×0 的隐藏窗口，生命周期完全由本类掌握。</para>
/// <para><b>L7 —— 这是全项目最危险的一段代码。</b><c>RegisterHotKey</c> 是全局独占：
/// 注册了哪个键，那个键就**不再传给 WPS**。误注册方向键 = 操作员再也翻不了 PPT。
/// 所以这里有两道闸：<see cref="HotkeyWhitelist"/> 先把配置里的键名过一遍，
/// 然后 <see cref="ToVirtualKey"/> 的映射表里**只有那五个键**——
/// 表里没有的键名根本得不到键码，压根注册不出去。</para>
/// </remarks>
internal sealed class GlobalHotkeyService : IDisposable
{
    private readonly Dictionary<int, HotkeyAction> _actions = new();
    private readonly List<int> _registeredIds = [];

    private HwndSource? _source;
    private bool _disposed;

    /// <summary>热键被按下。在 UI 线程上触发。</summary>
    internal event EventHandler<HotkeyAction>? Pressed;

    /// <summary>已注册的键位，供诊断显示（让操作员确认我们没多拿键）。</summary>
    internal IReadOnlyList<string> RegisteredKeys { get; private set; } = [];

    /// <summary>
    /// 按配置注册全部热键。必须在 UI 线程调用（该线程有消息循环）。
    /// </summary>
    internal HotkeyRegistrationResult Register(HotkeyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ThrowIfDisposed();

        EnsureSink();

        var registered = new List<string>();
        var failed = new List<string>();

        // id 从 1 开始：RegisterHotKey 的 id 在 0x0000–0xBFFF 范围内由应用自行分配。
        TryRegister(1, HotkeyAction.PrevPage, config.PrevPage, registered, failed);
        TryRegister(2, HotkeyAction.NextPage, config.NextPage, registered, failed);
        TryRegister(3, HotkeyAction.SendZh, config.SendZh, registered, failed);
        TryRegister(4, HotkeyAction.SendEn, config.SendEn, registered, failed);
        TryRegister(5, HotkeyAction.Clear, config.Clear, registered, failed);

        RegisteredKeys = registered;

        AppLog.Info(
            $"全局热键注册完成。已占用：{(registered.Count == 0 ? "无" : string.Join(" ", registered))}；" +
            $"失败：{(failed.Count == 0 ? "无" : string.Join(" ", failed))}。" +
            "方向键 / PgUp / PgDn / Space / Enter / Esc / B / W / F5 未被注册，仍归放映软件。");

        return new HotkeyRegistrationResult(registered, failed);
    }

    private void TryRegister(
        int id,
        HotkeyAction action,
        string keyName,
        List<string> registered,
        List<string> failed)
    {
        // 第一道闸：白名单。配置文件不是可信输入。
        if (!HotkeyWhitelist.IsAllowed(keyName))
        {
            AppLog.Error(
                $"拒绝注册「{keyName}」——不在允许的键位内（只许 {HotkeyWhitelist.AllowedList}）。" +
                "注册放映软件的按键会让操作员无法翻页。");
            failed.Add(keyName);
            return;
        }

        // 第二道闸：映射表里只有那五个键。
        uint? vk = ToVirtualKey(keyName);
        if (vk is null)
        {
            AppLog.Error($"键名「{keyName}」没有对应的虚拟键码，跳过。");
            failed.Add(keyName);
            return;
        }

        if (_source is null)
        {
            failed.Add(keyName);
            return;
        }

        bool ok = NativeMethods.RegisterHotKey(
            _source.Handle, id, NativeMethods.MOD_NOREPEAT, vk.Value);

        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            AppLog.Warn($"热键 {keyName} 注册失败（Win32 错误 {error}），可能已被其他程序占用。");
            failed.Add(keyName);
            return;
        }

        _actions[id] = action;
        _registeredIds.Add(id);
        registered.Add(HotkeyWhitelist.Canonicalize(keyName));
    }

    /// <summary>
    /// 键名 → 虚拟键码。**表里只有 F7 F8 F9 F10 F12**，这是有意的（L7）。
    /// 想加键位必须同时改 <see cref="HotkeyWhitelist"/> 和这里，两处都得过。
    /// </summary>
    private static uint? ToVirtualKey(string keyName) =>
        HotkeyWhitelist.Canonicalize(keyName) switch
        {
            "F7" => NativeMethods.VK_F7,
            "F8" => NativeMethods.VK_F8,
            "F9" => NativeMethods.VK_F9,
            "F10" => NativeMethods.VK_F10,
            "F12" => NativeMethods.VK_F12,
            _ => null,
        };

    /// <summary>
    /// 创建一个 0×0、无 WS_VISIBLE 的隐藏窗口收 <c>WM_HOTKEY</c>。
    /// 加 <c>WS_EX_TOOLWINDOW</c> 是为了确保它绝不会在 Alt+Tab 里冒出来。
    /// </summary>
    private void EnsureSink()
    {
        if (_source is not null)
        {
            return;
        }

        // 位置实参，不用命名实参：这个 7 参构造的形参名不值得赌
        // （classStyle, style, exStyle, x, y, name, parent）。
        _source = new HwndSource(
            0,                                  // classStyle
            0,                                  // style（不含 WS_VISIBLE，所以不可见）
            NativeMethods.WS_EX_TOOLWINDOW,     // exStyle：确保不进 Alt+Tab
            0,                                  // x
            0,                                  // y
            "PulpitHotkeySink",                 // name
            IntPtr.Zero);                       // parent

        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        if (!_actions.TryGetValue(wParam.ToInt32(), out HotkeyAction action))
        {
            return IntPtr.Zero;
        }

        handled = true;

        // WndProc 里抛出的异常会顺着 Win32 消息泵往上走，行为不可预期。
        // 直播中弹未处理异常对话框是事故，所以这里就地兜住。
        try
        {
            Pressed?.Invoke(this, action);
        }
        catch (Exception ex)
        {
            AppLog.Error($"处理热键 {action} 时异常（已吞掉，程序继续运行）。", ex);
        }

        return IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GlobalHotkeyService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_source is not null)
        {
            foreach (int id in _registeredIds)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            }

            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _registeredIds.Clear();
        _actions.Clear();

        AppLog.Info("全局热键已全部注销。");
    }
}
