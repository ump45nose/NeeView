using SharpCompress.Archives;
using SharpCompress.Readers;
using NeeView;
namespace NeeView.Backends;

/// <summary>原 Archive 工厂的 Mac 实现，P1 只开放目录与 ZIP。</summary>
public sealed class ArchiveFactory : IArchiveFactory
{
    /// <summary>后台列出当前目录直接子目录，不随同目录翻页重复调用。</summary>
    public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => SourceIo.RunAsync<IReadOnlyList<FolderItem>>(() =>
        new DirectoryInfo(path).EnumerateDirectories().Where(e => !e.Name.StartsWith('.')).Select(e => { token.ThrowIfCancellationRequested(); return new FolderItem(e.Name, e.FullName); }).OrderBy(e => e.Name, NaturalSort.Comparer).ToList(), token);
    /// <summary>图片转所在目录；不支持的归档返回明确提示。</summary>
    public Task<Archive> OpenAsync(string path, CancellationToken token) => SourceIo.RunAsync<Archive>(() =>
    {
        token.ThrowIfCancellationRequested(); path = System.IO.Path.GetFullPath(path);
        if (ImageFormats.IsImage(path))
        {
            if (!File.Exists(path)) throw new FileNotFoundException("图片不存在。", path);
            path = System.IO.Path.GetDirectoryName(path)!;
        }
        if (Directory.Exists(path)) return new FolderArchive(path);
        if (!File.Exists(path)) throw new FileNotFoundException("来源不存在。", path);
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".cbz") return new ZipArchive(path);
        throw new NotSupportedException("P1 支持目录、图片及 ZIP/CBZ；其他来源尚未迁移。");
    }, token);
}

/// <summary>有界文件系统任务；超时后继续观察不可中断调用并释放晚到资源。</summary>
public static class SourceIo
{
    private static readonly SemaphoreSlim Slots = new(2);
    /// <summary>后台运行同步系统调用；十五秒等待超时不会释放仍在使用的槽。</summary>
    public static async Task<T> RunAsync<T>(Func<T> action, CancellationToken token)
    {
        if (!await Slots.WaitAsync(TimeSpan.FromSeconds(15), token)) throw new TimeoutException("来源暂不可访问：I/O 队列等待超时。");
        var work = Task.Run(() => { try { return action(); } finally { Slots.Release(); } });
        try { return await work.WaitAsync(TimeSpan.FromSeconds(15), token); }
        catch
        {
            _ = ObserveLateAsync(work);
            throw;
        }
    }
    /// <summary>清理超时或取消后返回的来源/流，观察任务异常。</summary>
    private static async Task ObserveLateAsync<T>(Task<T> work)
    {
        try
        {
            var value = await work;
            if (value is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (value is IDisposable disposable) disposable.Dispose();
        }
        catch { /* 调用方已经收到读取失败；晚到任务只承担清理。 */ }
    }
}

/// <summary>原 FolderArchive 的普通目录分支；直接条目元数据与请求级文件流。</summary>
public sealed class FolderArchive(string path) : Archive(path)
{
    public override bool IsDirectory => true;
    /// <summary>按原 DirectoryInfo 枚举，过滤隐藏文件；不持有全目录文件句柄。</summary>
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => SourceIo.RunAsync<IReadOnlyList<ArchiveEntry>>(() =>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var entries = new List<ArchiveEntry>();
        foreach (var info in new DirectoryInfo(Path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (info.Name.StartsWith('.')) continue;
            entries.Add(new(this)
            {
                Id = entries.Count,
                RawEntryName = info.Name,
                FilePath = info.FullName,
                IsDirectory = info is DirectoryInfo,
                Length = info is FileInfo file ? file.Length : -1,
                LastWriteTime = info.LastWriteTime
            });
        }
        return entries;
    }, token);
    /// <summary>只为当前请求打开共享读取流，所有权交给调用方。</summary>
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => SourceIo.RunAsync<Stream>(() =>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new FileStream(entry.FilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }, token);
    /// <summary>目录来源本身没有长期文件句柄。</summary>
    public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
}

/// <summary>SharpCompress ZIP 后端，沿用 ArchiveEntry ID 区分重复名称。</summary>
public sealed class ZipArchive : Archive
{
    private readonly IArchive _archive;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<IArchiveEntry> _entries;
    /// <summary>打开只读 ZIP，不按归档路径创建本地文件。</summary>
    public ZipArchive(string path) : base(path)
    {
        _archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(path, new ReaderOptions());
        try { _entries = _archive.Entries.ToList(); }
        catch { _archive.Dispose(); throw; }
    }
    /// <summary>返回稳定的 entry ID 和原条目元数据。</summary>
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => SourceIo.RunAsync<IReadOnlyList<ArchiveEntry>>(() =>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var entries = new List<ArchiveEntry>();
        for (int id = 0; id < _entries.Count; id++)
        {
            token.ThrowIfCancellationRequested(); var entry = _entries[id];
            if (entry.IsEncrypted) throw new NotSupportedException("密码归档尚未迁移。");
            entries.Add(new(this)
            {
                Id = id,
                RawEntryName = entry.Key ?? "",
                IsDirectory = entry.IsDirectory,
                Length = entry.Size,
                LastWriteTime = entry.LastModifiedTime ?? DateTime.MinValue
            });
        }
        return entries;
    }, token);
    /// <summary>串行解压到请求级可定位流；大条目使用自动删除的随机临时文件。</summary>
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => SourceIo.RunAsync(() =>
    {
        // 探测入口也可能来自 UI；同步原生解压必须在后台 I/O 槽内运行。
        _gate.Wait(token);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (entry.Length > 2L * 1024 * 1024 * 1024) throw new NotSupportedException("条目超过临时解压预算。");
            using var input = _entries[entry.Id].OpenEntryStream();
            Stream output;
            if (entry.Length <= 32L * 1024 * 1024) output = new MemoryStream();
            else
            {
                var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NeeView.Mac"); Directory.CreateDirectory(directory);
                output = new FileStream(System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            }
            try
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested(); total += read;
                    if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("解压内容超出预算。");
                    output.Write(buffer, 0, read);
                }
                output.Position = 0; return output;
            }
            catch { output.Dispose(); throw; }
        }
        // 超时只取消调用方等待；工作真正结束后才允许 Dispose 关闭归档。
        finally { _gate.Release(); }
    }, token);
    /// <summary>等待当前解压结束后关闭归档，不中途释放原生来源。</summary>
    public override async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { if (!IsDisposed) { IsDisposed = true; _archive.Dispose(); } }
        finally { _gate.Release(); }
    }
}
