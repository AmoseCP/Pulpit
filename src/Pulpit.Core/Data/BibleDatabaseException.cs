using System;

namespace Pulpit.Core.Data;

/// <summary>
/// 数据库缺失、损坏或结构不符时抛出。
/// </summary>
/// <remarks>
/// M1 验收标准明确要求：数据库文件缺失/损坏时抛出**明确异常**，
/// 不是 <see cref="NullReferenceException"/>。所以所有底层 SqliteException
/// 都在 <see cref="BibleRepository"/> 里被包成本类型，并带上库文件路径。
/// </remarks>
public sealed class BibleDatabaseException : Exception
{
    public BibleDatabaseException(string message)
        : base(message)
    {
    }

    public BibleDatabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
