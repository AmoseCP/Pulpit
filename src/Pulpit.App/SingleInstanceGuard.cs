using System;
using System.Threading;

namespace Pulpit.App;

/// <summary>
/// 单实例守卫（L15 / P0-14）。
/// </summary>
/// <remarks>
/// <para>存在的理由很具体：两份实例会争抢全局热键——<c>RegisterHotKey</c> 是全局独占，
/// 第二份进程的注册会**静默失败**，操作员按 F9 没反应却看不出为什么。</para>
/// <para>互斥体用 <b>Local</b> 作用域（不加 <c>Global\</c> 前缀）。热键的冲突范围本来
/// 就是「同一个登录会话」，用 Global 反而会在某些环境里遇到创建权限问题，
/// 换来的是一个我们并不需要的跨会话保证。</para>
/// </remarks>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Pulpit.SingleInstance.9F2C4A1B";

    private Mutex? _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// 试着成为唯一实例。返回 false 表示已经有一份在跑，调用方应提示并退出。
    /// </summary>
    internal static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        guard = null;

        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (createdNew)
        {
            guard = new SingleInstanceGuard(mutex);
            return true;
        }

        // 没拿到所有权：另一份实例正持有它。
        mutex.Dispose();
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 当前线程并不持有它——退出路径上不值得为此中断。
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
