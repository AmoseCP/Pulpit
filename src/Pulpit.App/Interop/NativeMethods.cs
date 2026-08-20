using System;
using System.Runtime.InteropServices;

namespace Pulpit.App.Interop;

/// <summary>
/// 全项目**唯一**允许声明 <c>DllImport</c> 的位置（见 CLAUDE.md「代码约定」）。
/// 其他任何文件不得直接写 P/Invoke。
/// </summary>
internal static class NativeMethods
{
    // ---------- GetWindowLong / SetWindowLong ----------

    internal const int GWL_EXSTYLE = -20;

    /// <summary>逐像素透明合成。WPF 的 AllowsTransparency=true 已经会加上这一位。</summary>
    internal const int WS_EX_LAYERED = 0x0008_0000;

    /// <summary>鼠标穿透：点击事件落到下层的放映软件。</summary>
    internal const int WS_EX_TRANSPARENT = 0x0000_0020;

    /// <summary>不进 Alt+Tab 列表。</summary>
    internal const int WS_EX_TOOLWINDOW = 0x0000_0080;

    /// <summary>
    /// **绝不能少**：窗口永不获取焦点。缺失时叠加层一旦被激活，
    /// 放映软件会判定失焦而退出全屏——现场直接事故（L5）。
    /// </summary>
    internal const int WS_EX_NOACTIVATE = 0x0800_0000;

    /// <summary>与 TOOLWINDOW 互斥；出现在 Alt+Tab 里就是因为它，必须清掉。</summary>
    internal const int WS_EX_APPWINDOW = 0x0004_0000;

    // ---------- SetWindowPos ----------

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    // 本程序发布目标恒为 win-x64（L1），因此直接绑 *Ptr 版本，不做 32 位分支。
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // ---------- RegisterHotKey ----------

    internal const int WM_HOTKEY = 0x0312;

    /// <summary>
    /// 按住不放只触发一次。没有这一位，按住 F8 会连续翻页翻到末页。
    /// </summary>
    internal const uint MOD_NOREPEAT = 0x4000;

    // 虚拟键码。**只列白名单里的五个**（L7）——表里没有的键就没法被映射成键码，
    // 这是「误注册方向键」这个最严重回归的第二道闸（第一道是 HotkeyWhitelist）。
    internal const uint VK_F7 = 0x76;
    internal const uint VK_F8 = 0x77;
    internal const uint VK_F9 = 0x78;
    internal const uint VK_F10 = 0x79;
    internal const uint VK_F12 = 0x7B;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---------- 托管侧便捷封装 ----------

    internal static int GetWindowExStyle(IntPtr hWnd)
        => (int)GetWindowLongPtrW(hWnd, GWL_EXSTYLE).ToInt64();

    internal static void SetWindowExStyle(IntPtr hWnd, int exStyle)
        => SetWindowLongPtrW(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
}
