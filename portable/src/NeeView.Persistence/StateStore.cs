using System.Text.Json;
using Microsoft.Data.Sqlite;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Persistence;

public static class StoreJson
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("状态为空。");
}
/// <summary>独立 macOS JSON 设置，使用同目录临时文件原子替换。</summary>
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private readonly SemaphoreSlim _gate = new(1);
    public string Path { get; } = path;
    /// <summary>在同一互斥内读取并合并字段；并发的阅读来源与面板配置互不覆盖。</summary>
    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current = File.Exists(Path) ? StoreJson.Read<AppSettings>(await File.ReadAllTextAsync(Path, token)) : new();
            if (current.Version > 1) throw new InvalidDataException("设置版本高于当前程序，不能覆盖。");
            var next = update(current);
            await WriteAsync(next, token);
            return next;
        }
        finally { _gate.Release(); }
    }
    /// <summary>读取和验证版本；不覆盖损坏或未来版本的文件。</summary>
    public async Task<AppSettings> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(Path)) return new();
            var settings = StoreJson.Read<AppSettings>(await File.ReadAllTextAsync(Path, token));
            if (settings.Version > 1) throw new InvalidDataException("设置版本高于当前程序，不能覆盖。");
            return settings with { MoveHistoryCapacity = Math.Clamp(settings.MoveHistoryCapacity, 0, 1000),
                DestinationRatio = double.IsFinite(settings.DestinationRatio) ? Math.Clamp(settings.DestinationRatio, 0.1, 0.9) : 0.5 };
        }
        finally { _gate.Release(); }
    }
    /// <summary>写入并刷新临时文件，再原子替换目标。</summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await WriteAsync(settings, token); }
        finally { _gate.Release(); }
    }
    /// <summary>调用方持有设置锁，写入临时文件并原子替换。</summary>
    private async Task WriteAsync(AppSettings settings, CancellationToken token)
    {
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, StoreJson.Options, token);
                await stream.FlushAsync(token); stream.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>进程共享 SQLite 状态及身份登记。连接操作串行，数据库更新留版本化 SQL。</summary>
public sealed class SqliteStateStore : IReaderStateStore, IIdentityRegistry, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1);
    public string DatabasePath { get; }
    private bool _disposed;
    public SqliteStateStore(string path)
    {
        DatabasePath = path; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _connection.Open();
        using var pragmas = _connection.CreateCommand();
        pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; pragmas.ExecuteNonQuery();
        using var migration = _connection.CreateCommand();
        using var sql = typeof(SqliteStateStore).Assembly.GetManifestResourceStream("NeeView.Persistence.Sql.001_initial.sql")!;
        using var reader = new StreamReader(sql); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
    }
    /// <summary>同一连接上的工作通过互斥执行，避免并发事务。</summary>
    internal async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, Task<T>> action, CancellationToken token = default)
    {
        await _gate.WaitAsync(token); try { return await action(_connection); } finally { _gate.Release(); }
    }
    /// <summary>定位键保留大小写和内部路径；不按 Windows 规则转换 macOS 文件系统。</summary>
    internal static string LocatorKey(SourceLocator locator) => StoreJson.Serialize(locator);
    /// <summary>登记或恢复路径对应的稳定身份。</summary>
    private Task<string> IdentityAsync(string kind, SourceLocator locator, CancellationToken token) => WithConnectionAsync(async connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO identities(kind,locator,id) VALUES($kind,$locator,$id); SELECT id FROM identities WHERE kind=$kind AND locator=$locator;";
        command.Parameters.AddWithValue("$kind", kind); command.Parameters.AddWithValue("$locator", LocatorKey(locator));
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        return (string)(await command.ExecuteScalarAsync(token))!;
    }, token);
    public async Task<BookId> GetBookAsync(SourceLocator locator, CancellationToken token) => new(await IdentityAsync("book", locator, token));
    public async Task<ContentId> GetContentAsync(SourceLocator locator, CancellationToken token) => new(await IdentityAsync("content", locator, token));
    /// <summary>应用内移动后保留身份并更新关联的历史和书签定位。</summary>
    public Task RelocateAsync(SourceLocator oldLocator, SourceLocator newLocator, CancellationToken token) => WithConnectionAsync(async connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        // 幂等恢复：源身份已迁移时不得再次删除目标身份。
        using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
        lookup.CommandText = "SELECT id FROM identities WHERE kind='content' AND locator=$old";
        lookup.Parameters.AddWithValue("$old", LocatorKey(oldLocator));
        var movedId = await lookup.ExecuteScalarAsync(token) as string;
        if (movedId is null) { transaction.Commit(); return true; }
        var targetBookLocator = new SourceLocator(Path.GetDirectoryName(newLocator.Path)!);
        using var registerBook = connection.CreateCommand(); registerBook.Transaction = transaction;
        registerBook.CommandText = "INSERT OR IGNORE INTO identities(kind,locator,id) VALUES('book',$locator,$id); SELECT id FROM identities WHERE kind='book' AND locator=$locator;";
        registerBook.Parameters.AddWithValue("$locator", LocatorKey(targetBookLocator)); registerBook.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        var targetBook = new BookId((string)(await registerBook.ExecuteScalarAsync(token))!);
        // 被覆盖的旧定位先失效，历史记录仍保留原身份，不能误指向新内容。
        command.CommandText = "DELETE FROM identities WHERE kind='content' AND locator=$new AND EXISTS(SELECT 1 FROM identities WHERE kind='content' AND locator=$old); UPDATE identities SET locator=$new WHERE kind='content' AND locator=$old;";
        command.Parameters.AddWithValue("$new", LocatorKey(newLocator)); command.Parameters.AddWithValue("$old", LocatorKey(oldLocator));
        await command.ExecuteNonQueryAsync(token);
        var migratedStates = new List<ReadingState>();
        foreach (var table in new[] { "reading_states", "bookmarks" })
        {
            using var read = connection.CreateCommand(); read.Transaction = transaction;
            read.CommandText = $"SELECT {(table == "bookmarks" ? "id" : "book_id")},data FROM {table}";
            var updates = new List<(string Id, string Data)>();
            using (var rows = await read.ExecuteReaderAsync(token))
            {
                while (await rows.ReadAsync(token))
                {
                    var data = rows.GetString(1);
                    if (table == "bookmarks")
                    {
                        var bookmark = StoreJson.Read<Bookmark>(data);
                        if (bookmark.Anchor?.Content.Value == movedId || bookmark.Locator?.Path == Path.GetDirectoryName(oldLocator.Path) && bookmark.LegacyPage?.Replace('\\', '/') == Path.GetFileName(oldLocator.Path))
                            updates.Add((rows.GetString(0), StoreJson.Serialize(bookmark with { Locator = targetBookLocator, Book = targetBook, LegacyPage = bookmark.LegacyPage is null ? null : Path.GetFileName(newLocator.Path) })));
                        else if (bookmark.Locator == oldLocator) updates.Add((rows.GetString(0), StoreJson.Serialize(bookmark with { Locator = newLocator })));
                    }
                    else
                    {
                        var state = StoreJson.Read<ReadingState>(data);
                        if (state.Anchor?.Content.Value == movedId || state.Locator == oldLocator)
                            migratedStates.Add(state with { Book = targetBook, Locator = targetBookLocator, LegacyPage = state.LegacyPage is null ? null : Path.GetFileName(newLocator.Path) });
                    }
                }
            }
            foreach (var update in updates)
            {
                using var write = connection.CreateCommand(); write.Transaction = transaction;
                write.CommandText = $"UPDATE {table} SET data=$data WHERE {(table == "bookmarks" ? "id" : "book_id")}=$id";
                write.Parameters.AddWithValue("$data", update.Data); write.Parameters.AddWithValue("$id", update.Id); await write.ExecuteNonQueryAsync(token);
            }
        }
        // 目标目录已有更近期阅读状态时保留它；书签仍直接指向移动图片身份。
        foreach (var state in migratedStates)
        {
            using var write = connection.CreateCommand(); write.Transaction = transaction;
            write.CommandText = "INSERT INTO reading_states VALUES($id,$data,$time) ON CONFLICT(book_id) DO UPDATE SET data=$data,accessed=$time WHERE excluded.accessed >= reading_states.accessed";
            write.Parameters.AddWithValue("$id", state.Book.Value); write.Parameters.AddWithValue("$data", StoreJson.Serialize(state)); write.Parameters.AddWithValue("$time", state.LastAccess.ToString("O")); await write.ExecuteNonQueryAsync(token);
        }
        transaction.Commit(); return true;
    }, token);
    /// <summary>按书籍身份读取保存的锚点和设置。</summary>
    public Task<ReadingState?> GetAsync(BookId book, CancellationToken token = default) => WithConnectionAsync(async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT data FROM reading_states WHERE book_id=$id";
        command.Parameters.AddWithValue("$id", book.Value); var data = await command.ExecuteScalarAsync(token);
        return data is string json ? StoreJson.Read<ReadingState>(json) : null;
    }, token);
    /// <summary>保存同一本书共享的阅读状态，最近访问用于历史排序。</summary>
    public Task SaveAsync(ReadingState state, CancellationToken token = default) => WithConnectionAsync(async connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO reading_states VALUES($id,$data,$time) ON CONFLICT(book_id) DO UPDATE SET data=$data,accessed=$time";
        command.Parameters.AddWithValue("$id", state.Book.Value); command.Parameters.AddWithValue("$data", StoreJson.Serialize(state));
        command.Parameters.AddWithValue("$time", state.LastAccess.ToString("O")); await command.ExecuteNonQueryAsync(token); return true;
    }, token);
    /// <summary>读取 JSON 行；SQL 来源为程序固定常量。</summary>
    private Task<IReadOnlyList<T>> ListAsync<T>(string sql, CancellationToken token) => WithConnectionAsync<IReadOnlyList<T>>(async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync(token); var result = new List<T>();
        while (await reader.ReadAsync(token)) result.Add(StoreJson.Read<T>(reader.GetString(0)));
        return result;
    }, token);
    public Task<IReadOnlyList<ReadingState>> HistoryAsync(CancellationToken token = default) => ListAsync<ReadingState>("SELECT data FROM reading_states ORDER BY accessed DESC", token);
    public Task<IReadOnlyList<Bookmark>> BookmarksAsync(CancellationToken token = default) => ListAsync<Bookmark>("SELECT data FROM bookmarks ORDER BY parent_id,sort_order", token);
    /// <summary>保存树节点的父子和顺序；叶节点共享书籍状态。</summary>
    public Task SaveBookmarkAsync(Bookmark bookmark, CancellationToken token = default) => WithConnectionAsync(async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO bookmarks VALUES($id,$parent,$order,$data) ON CONFLICT(id) DO UPDATE SET parent_id=$parent,sort_order=$order,data=$data";
        command.Parameters.AddWithValue("$id", bookmark.Id); command.Parameters.AddWithValue("$parent", (object?)bookmark.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$order", bookmark.Order); command.Parameters.AddWithValue("$data", StoreJson.Serialize(bookmark));
        await command.ExecuteNonQueryAsync(token); return true;
    }, token);
    /// <summary>删除书签节点及所有后代。</summary>
    public Task DeleteBookmarkAsync(string id, CancellationToken token = default) => ExecuteAsync(
        "WITH RECURSIVE subtree(id) AS (SELECT id FROM bookmarks WHERE id=$id UNION ALL SELECT b.id FROM bookmarks b JOIN subtree s ON b.parent_id=s.id) DELETE FROM bookmarks WHERE id IN subtree", id, null, token);
    public Task RecordOperationAsync(RecoveryOperation operation, CancellationToken token = default) => ExecuteAsync(
        "INSERT INTO file_operations VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=$data", operation.Id, StoreJson.Serialize(operation), token);
    public Task RemoveOperationAsync(string id, CancellationToken token = default) => ExecuteAsync("DELETE FROM file_operations WHERE id=$id", id, null, token);
    public Task<IReadOnlyList<RecoveryOperation>> RecoveriesAsync(CancellationToken token = default) => ListAsync<RecoveryOperation>("SELECT data FROM file_operations", token);
    /// <summary>执行固定参数化 SQL，禁止拼接外部输入。</summary>
    private Task ExecuteAsync(string sql, string id, string? data, CancellationToken token) => WithConnectionAsync(async connection =>
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$id", id);
        if (data is not null) command.Parameters.AddWithValue("$data", data);
        await command.ExecuteNonQueryAsync(token); return true;
    }, token);
    /// <summary>使用 SQLite 在线备份 API，确保 WAL 中的记录也进入备份。</summary>
    public Task BackupAsync(string target, CancellationToken token = default) => WithConnectionAsync(connection =>
    {
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target }.ToString()); backup.Open();
        connection.BackupDatabase(backup); return Task.FromResult(true);
    }, token);
    /// <summary>等待状态写入结束后关闭连接。</summary>
    public async ValueTask DisposeAsync() { await _gate.WaitAsync(); try { if (_disposed) return; _disposed = true; await _connection.DisposeAsync(); } finally { _gate.Release(); } }
}
