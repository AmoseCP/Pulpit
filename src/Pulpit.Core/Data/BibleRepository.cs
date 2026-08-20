using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Pulpit.Core.Data;

/// <summary>
/// 只读 SQLite 数据访问。
/// </summary>
/// <remarks>
/// **只读，无例外**（L12）：连接串恒带 <c>Mode=ReadOnly</c>，不写入、不迁移、
/// 不设 <c>PRAGMA journal_mode=WAL</c>（WAL 需要写权限，且会在库旁生成 -wal/-shm 文件）。
/// <para>
/// 连接在构造时打开并保持——桌面单用户场景下，每次查询重开连接才是那个
/// 「冷启动首次查询 &lt; 50ms」过不去的原因。本类是 <see cref="IDisposable"/>，
/// 由宿主持有到进程结束。
/// </para>
/// <para>
/// 查询同步执行：SQLite 本地覆盖索引查询是微秒级，异步只会引入调度开销。
/// </para>
/// </remarks>
public sealed class BibleRepository : IBibleRepository, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _databasePath;
    private bool _disposed;

    public BibleRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);

        if (!File.Exists(_databasePath))
        {
            throw new BibleDatabaseException($"经文数据库不存在：{_databasePath}");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        _connection = new SqliteConnection(builder.ToString());

        try
        {
            _connection.Open();
        }
        catch (SqliteException ex)
        {
            _connection.Dispose();
            throw new BibleDatabaseException($"经文数据库无法打开（可能已损坏）：{_databasePath}", ex);
        }

        try
        {
            SchemaVersion = ReadSchemaVersion();
        }
        catch (SqliteException ex)
        {
            _connection.Dispose();
            throw new BibleDatabaseException(
                $"经文数据库结构不符（读不到 meta.schema_version）：{_databasePath}", ex);
        }
    }

    /// <summary>库的 <c>meta.schema_version</c>，供状态栏显示 DB 版本。</summary>
    public string SchemaVersion { get; }

    public string DatabasePath => _databasePath;

    private string ReadSchemaVersion()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version';";

        object? value = cmd.ExecuteScalar();
        return value is null
            ? throw new BibleDatabaseException($"经文数据库缺少 meta.schema_version：{_databasePath}")
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
    }

    public int? ResolveBook(string normalizedAlias)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(normalizedAlias))
        {
            return null;
        }

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT book_id FROM book_aliases WHERE alias = $alias;";
        cmd.Parameters.AddWithValue("$alias", normalizedAlias);

        object? value = Execute(cmd);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public (int Chapters, string NameZh)? GetBookInfo(int bookId)
    {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT chapters, name_zh FROM books WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", bookId);

        using SqliteDataReader reader = ExecuteReader(cmd);
        return reader.Read()
            ? (reader.GetInt32(0), reader.GetString(1))
            : null;
    }

    public int? GetVerseCount(int bookId, int chapter)
    {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT verse_count FROM chapter_info WHERE book_id = $book AND chapter = $chapter;";
        cmd.Parameters.AddWithValue("$book", bookId);
        cmd.Parameters.AddWithValue("$chapter", chapter);

        object? value = Execute(cmd);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<VerseText> Lookup(VerseRef reference, int transId = 1)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(reference);

        int low = reference.Verse;
        int high = reference.EndVerse ?? reference.Verse;

        if (high < low)
        {
            (low, high) = (high, low);
        }

        using SqliteCommand cmd = _connection.CreateCommand();

        // GROUP BY v.merge_head 就是并节去重：一个并节组只出一行，
        // 因此 民1:20-21 出 1 条而不是 2 条（→ 1 页而不是 2 页）。
        // 组内每个节号的 text_display / merge_* 都相同，取哪一行都一样。
        cmd.CommandText = """
            SELECT v.book_id, b.name_zh, v.chapter, v.merge_head, v.merge_last,
                   v.text_display, v.text_raw
            FROM verses v
            JOIN books b ON b.id = v.book_id
            WHERE v.trans_id = $trans
              AND v.book_id = $book
              AND v.chapter = $chapter
              AND v.verse BETWEEN $low AND $high
            GROUP BY v.merge_head
            ORDER BY v.merge_head;
            """;

        cmd.Parameters.AddWithValue("$trans", transId);
        cmd.Parameters.AddWithValue("$book", reference.BookId);
        cmd.Parameters.AddWithValue("$chapter", reference.Chapter);
        cmd.Parameters.AddWithValue("$low", low);
        cmd.Parameters.AddWithValue("$high", high);

        var results = new List<VerseText>();

        using SqliteDataReader reader = ExecuteReader(cmd);
        while (reader.Read())
        {
            results.Add(new VerseText(
                BookId: reader.GetInt32(0),
                BookNameZh: reader.GetString(1),
                Chapter: reader.GetInt32(2),
                MergeHead: reader.GetInt32(3),
                MergeLast: reader.GetInt32(4),
                TextDisplay: reader.GetString(5),
                TextRaw: reader.GetString(6)));
        }

        return results;
    }

    // ---- 把底层 SqliteException 统一包成 BibleDatabaseException ----

    private object? Execute(SqliteCommand cmd)
    {
        try
        {
            return cmd.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            throw new BibleDatabaseException($"经文数据库查询失败：{_databasePath}", ex);
        }
    }

    private SqliteDataReader ExecuteReader(SqliteCommand cmd)
    {
        try
        {
            return cmd.ExecuteReader();
        }
        catch (SqliteException ex)
        {
            throw new BibleDatabaseException($"经文数据库查询失败：{_databasePath}", ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BibleRepository));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }
}
