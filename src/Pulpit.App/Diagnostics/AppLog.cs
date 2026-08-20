using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pulpit.App.Diagnostics;

/// <summary>
/// 最小日志。写 <c>%LOCALAPPDATA%\Pulpit\logs\pulpit-yyyyMMdd.log</c>。
/// </summary>
/// <remarks>
/// 存在的唯一理由：CLAUDE.md「绝不允许的行为」第一条——直播中不得弹未处理异常对话框，
/// 异常一律捕获 → 写日志 → 继续运行。所以本类自身**永不抛异常**：
/// 写盘失败就静默丢弃，日志故障绝不能把主程序带下去。
/// </remarks>
internal static class AppLog
{
    private static readonly object Gate = new();

    internal static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pulpit",
        "logs");

    internal static string CurrentLogPath =>
        Path.Combine(LogDirectory, $"pulpit-{DateTime.Now:yyyyMMdd}.log");

    internal static void Info(string message) => Write("INFO ", message);

    internal static void Warn(string message) => Write("WARN ", message);

    internal static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(level)
                .Append(' ')
                .Append(message)
                .Append(Environment.NewLine)
                .ToString();

            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 故意吞掉：日志写不进去不是停机理由。
        }
    }
}
