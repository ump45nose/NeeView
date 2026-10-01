namespace NeeView.Content;

/// <summary>进程级解压磁盘预算；流租约占用的文件不能回收，来源关闭后最后一个流释放再删除。</summary>
internal static class ArchiveCacheBudget
{
    private sealed class Entry(long bytes) { public long Bytes = bytes; public int Readers; public bool Retired; public bool Writing = true; public long Used; }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = [];
    private static long _sequence;
    private const long Limit = 2L * 1024 * 1024 * 1024;
    /// <summary>输入应用随机路径及声明大小，预留输出空间；只有无人读取的缓存可回收。</summary>
    public static void Reserve(string path, long bytes)
    {
        lock (Gate)
        {
            if (bytes < 0 || bytes > Limit) throw new InvalidDataException("归档条目超过解压预算。");
            var total = Entries.Values.Sum(e => e.Bytes);
            foreach (var pair in Entries.Where(e => e.Value.Readers == 0 && !e.Value.Writing).OrderBy(e => e.Value.Used).ToArray())
            {
                if (total + bytes <= Limit) break;
                File.Delete(pair.Key); total -= pair.Value.Bytes; Entries.Remove(pair.Key); RemoveEmptyDirectory(pair.Key);
            }
            if (total + bytes > Limit) throw new IOException("解压缓存正在使用，暂时无法满足磁盘预算。");
            Entries.Add(path, new(bytes) { Used = ++_sequence });
        }
    }
    /// <summary>打开缓存流时增加引用；返回流独占所有权，关闭回调负责归还。</summary>
    public static Stream Open(string path)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(path, out var entry)) throw new FileNotFoundException("解压缓存已回收。", path);
            var stream = new CacheStream(path, () => Release(path)); entry.Writing = false; entry.Readers++; entry.Used = ++_sequence; return stream;
        }
    }
    /// <summary>来源关闭或解压失败后标记删除；活动流最后释放才实际删除。</summary>
    public static void Retire(string path)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(path, out var entry)) return;
            entry.Retired = true;
            if (entry.Readers == 0) { File.Delete(path); Entries.Remove(path); RemoveEmptyDirectory(path); }
        }
    }
    /// <summary>归还流引用并处理延迟删除。</summary>
    private static void Release(string path)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(path, out var entry)) return;
            entry.Readers--;
            if (entry.Readers == 0 && entry.Retired) { File.Delete(path); Entries.Remove(path); RemoveEmptyDirectory(path); }
        }
    }
    /// <summary>仅清理本缓存登记路径的空父目录，保留还有其他租约的目录。</summary>
    private static void RemoveEmptyDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    /// <summary>FileStream 适配引用归还；同步和异步释放均幂等。</summary>
    private sealed class CacheStream(string path, Action release) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous)
    {
        private Action? _release = release;
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) Interlocked.Exchange(ref _release, null)?.Invoke(); }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); Interlocked.Exchange(ref _release, null)?.Invoke(); }
    }
}
