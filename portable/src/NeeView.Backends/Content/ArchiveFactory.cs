using SharpCompress.Archives;
using SharpCompress.Readers;
using NeeView;
namespace NeeView.Backends;

/// <summary>原 Archive 工厂的 Mac 实现，目录、ZIP、RAR 与 7z 共用原来源关系。</summary>
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
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"(?:\.part\d+\.rar|\.r\d{2}|\.\d{3})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new NotSupportedException("分卷归档尚未迁移，请使用完整的单文件归档。");
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".cbz" or ".rar" or ".cbr" or ".7z") return new CompressedArchive(path);
        throw new NotSupportedException("支持目录、图片、ZIP/CBZ、RAR/CBR 和 7z；其他来源尚未迁移。");
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

/// <summary>SharpCompress 归档后端，沿用 ArchiveEntry ID 区分重复名称。</summary>
public sealed class CompressedArchive : Archive
{
    private readonly IArchive _archive;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<IArchiveEntry> _entries;
    private readonly Dictionary<int, (string Path, long Length, long Used)> _solidFiles = [];
    private string? _cacheDirectory;
    private long _cacheClock;
    private const long DiskBudget = 2L * 1024 * 1024 * 1024;
    public long CachedBytes => _solidFiles.Values.Sum(e => e.Length);
    // 7z 的块读取器也持有解码状态，统一采用独立顺序抽取和请求结果缓存。
    private bool RequiresSequential => _archive.IsSolid || _archive.Type == SharpCompress.Common.ArchiveType.SevenZip;
    /// <summary>打开只读普通或固实归档，不按归档路径创建本地文件。</summary>
    public CompressedArchive(string path) : base(path)
    {
        _archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(path, new ReaderOptions());
        try
        {
            // ZIP/7z 的通用 Volume.IsMultiVolume 默认值不代表真实分卷；RAR 使用头部标志。
            if (_archive.Volumes.Count() > 1 || _archive.Volumes.OfType<SharpCompress.Common.Rar.RarVolume>().Any(v => v.IsMultiVolume))
                throw new NotSupportedException("分卷归档尚未迁移。");
            _entries = _archive.Entries.ToList();
        }
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
            if (entry.Length > DiskBudget) throw new NotSupportedException("条目超过临时解压预算。");
            if (_solidFiles.TryGetValue(entry.Id, out var cached))
            {
                _solidFiles[entry.Id] = (cached.Path, cached.Length, ++_cacheClock);
                return (Stream)new FileStream(cached.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            // 固实包必须从块前部顺序恢复解码状态；不能直接调用随机条目流。
            // Reader 释放会关闭底层 SourceStream，固实抽取需独立来源，不能破坏登记索引的归档。
            using var extraction = RequiresSequential ? SharpCompress.Archives.ArchiveFactory.OpenArchive(Path, new ReaderOptions()) : null;
            using var reader = extraction?.ExtractAllEntries();
            if (reader is not null)
            {
                // 7z 索引可能把目录排在文件之前，Reader 则使用物理顺序；以名称与同名序号定位。
                int occurrence = _entries.Take(entry.Id + 1).Count(e => e.Key?.Replace('\\', '/') == entry.EntryName);
                bool found = false;
                while (reader.MoveToNextEntry())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.Entry.Key?.Replace('\\', '/') == entry.EntryName && --occurrence == 0) { found = true; break; }
                }
                if (!found) throw new InvalidDataException("固实归档条目无法定位。");
            }
            using var input = reader?.OpenEntryStream() ?? _entries[entry.Id].OpenEntryStream();
            Stream output;
            string? cachedPath = null;
            if (RequiresSequential)
            {
                _cacheDirectory ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NeeView.Mac", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_cacheDirectory);
                TrimSolidCache(Math.Max(0, entry.Length));
                cachedPath = System.IO.Path.Combine(_cacheDirectory, Guid.NewGuid().ToString("N"));
                output = new FileStream(cachedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
            }
            else if (entry.Length <= 32L * 1024 * 1024) output = new MemoryStream();
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
                    if (total > DiskBudget) throw new InvalidDataException("解压内容超出预算。");
                    output.Write(buffer, 0, read);
                }
                if (entry.Length >= 0 && total != entry.Length) throw new InvalidDataException("归档条目读取不完整，实际长度与索引不一致。");
                if (cachedPath is not null)
                {
                    TrimSolidCache(total); output.Flush();
                    _solidFiles[entry.Id] = (cachedPath, total, ++_cacheClock);
                }
                output.Position = 0; return output;
            }
            catch { output.Dispose(); if (cachedPath is not null) File.Delete(cachedPath); throw; }
        }
        // 超时只取消调用方等待；工作真正结束后才允许 Dispose 关闭归档。
        finally { _gate.Release(); }
    }, token);
    /// <summary>按来源的 2 GiB 预算回收最旧固实结果；已打开请求流仍由调用方拥有。</summary>
    private void TrimSolidCache(long incoming)
    {
        long total = CachedBytes;
        foreach (var pair in _solidFiles.OrderBy(e => e.Value.Used).ToArray())
        {
            if (total + incoming <= DiskBudget) break;
            File.Delete(pair.Value.Path); _solidFiles.Remove(pair.Key); total -= pair.Value.Length;
        }
    }
    /// <summary>等待当前解压结束后关闭归档，不中途释放原生来源。</summary>
    public override async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsDisposed) { IsDisposed = true; _archive.Dispose(); }
            if (_cacheDirectory is not null && Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, true);
            _solidFiles.Clear();
        }
        finally { _gate.Release(); }
    }
}
