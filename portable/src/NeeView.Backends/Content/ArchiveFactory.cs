using SharpCompress.Archives;
using SharpCompress.Readers;
using NeeView;
namespace NeeView.Backends;

/// <summary>原 Archive 工厂的 Mac 实现，目录、ZIP、RAR 与 7z 共用原来源关系。</summary>
public sealed partial class ArchiveFactory(Func<string, string?>? resolveAlias = null) : IArchiveFactory
{
    /// <summary>应用独占的解压临时根；启动装配将此目录交给历史保存排除，非系统临时根。</summary>
    public static string TemporaryDirectory => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NeeView.Mac");
    /// <summary>原书签 GetFileSystemInfo 替换点；仅后台探测指定路径，不枚举或解压。</summary>
    /// <param name="path">文件系统原定位。</param><param name="token">有界 I/O 的取消令牌。</param>
    /// <returns>真实时间及大小；缺失或非文件系统定位返回空。</returns>
    public Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => SourceIo.RunAsync<FolderItem?>(() =>
    {
        token.ThrowIfCancellationRequested();
        // GetAttributes 区分缺失和权限失败；不能将断线/权限错误默默解释为零大小。
        try
        {
            var attributes = File.GetAttributes(path);
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            return new(info.Name, info.FullName, info is DirectoryInfo, info is FileInfo file ? file.Length : -1, info.LastWriteTime) { IsSymbolicLink = (attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null };
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }, token);
    /// <summary>沿用原 FolderItemFactory 的普通目录/归档过滤；枚举元数据不递归也不解码封面。</summary>
    public async Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token)
    {
        var folders = await ListDirectoryBooksAsync(path, token); if (folders is not null) return folders;
        // 原归档书架共用ArchiveEntryCollection；逻辑目录不交给DirectoryInfo，也不解码封面。
        var source = await OpenAsync(path, token); await using var collection = new ArchiveEntryCollection(source, this, false);
        var entries = await collection.GetEntriesAsync(token);
        return entries.Where(e => e.ArchiveEntry.IsBook() && (collection.Mode == ArchiveEntryCollectionMode.CurrentDirectory || !e.ArchiveEntry.IsDirectory))
            .Select(e => new FolderItem(e.EntryName, e.ArchiveEntry.SystemPath, e.ArchiveEntry.IsDirectory, e.ArchiveEntry.Length, e.ArchiveEntry.LastWriteTime)).ToArray();
    }
    /// <summary>普通目录仅枚举直接子书；不存在的系统目录交由原归档逻辑来源解析。</summary>
    private static Task<IReadOnlyList<FolderItem>?> ListDirectoryBooksAsync(string path, CancellationToken token) => SourceIo.RunAsync<IReadOnlyList<FolderItem>?>(() =>
    {
        if (!Directory.Exists(path)) return null;
        var items = new List<FolderItem>();
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (info.Name.StartsWith('.')) continue;
            if (info is DirectoryInfo) items.Add(new(info.Name, info.FullName, true, -1, info.LastWriteTime) { IsSymbolicLink = info.LinkTarget is not null });
            else if (info is FileInfo file && ArchiveFormats.IsArchive(info.Name))
                items.Add(new(info.Name, info.FullName, false, file.Length, file.LastWriteTime));
        }
        return items;
    }, token);
    /// <summary>后台列出当前目录直接子目录，不随同目录翻页重复调用。</summary>
    public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => SourceIo.RunAsync<IReadOnlyList<FolderItem>>(() =>
        new DirectoryInfo(path).EnumerateDirectories().Where(e => !e.Name.StartsWith('.')).Select(e => { token.ThrowIfCancellationRequested(); return new FolderItem(e.Name, e.FullName); }).OrderBy(e => e.Name, NaturalSort.Comparer).ToList(), token);
    /// <summary>图片转所在目录；不支持的归档返回明确提示。</summary>
    public Task<Archive> OpenAsync(string path, CancellationToken token) => SourceIo.RunAsync<Archive>(() =>
    {
        token.ThrowIfCancellationRequested(); path = System.IO.Path.GetFullPath(path);
        // Finder打开别名的系统含义是打开其目标。原生解析只由启动装配注入，不模拟Windows.lnk。
        if (resolveAlias is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; File.Exists(path); depth++)
            {
                token.ThrowIfCancellationRequested(); var target = resolveAlias(path); if (target is null) break;
                if (depth >= 8 || !seen.Add(path)) throw new IOException("Finder别名循环或层级过深。");
                path = System.IO.Path.GetFullPath(target);
            }
        }
        // 原归档内逻辑路径：只在完整路径不存在时回溯真实归档，尊重名称含 .cbz 的普通目录。
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            var parent = System.IO.Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(parent))
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(parent) && ArchiveFormats.IsCompressedArchive(parent))
                {
                    var relative = System.IO.Path.GetRelativePath(parent, path);
                    var compressed = new CompressedArchive(parent);
                    try
                    {
                        if (compressed.ContainsDirectory(relative)) return new ArchiveDirectory(compressed, path, relative);
                        if (!compressed.ContainsFile(relative)) throw new FileNotFoundException("归档内部指定页面或目录不存在：" + relative);
                        if (ArchiveFormats.IsArchive(relative)) throw new NotSupportedException("嵌套归档尚未迁移。");
                        if (Config.Current.System.ArchiveRecursiveMode == ArchiveEntryCollectionMode.CurrentDirectory && relative.Contains('/'))
                        {
                            var slash = relative.LastIndexOf('/');
                            return new ArchiveDirectory(compressed, System.IO.Path.GetDirectoryName(path)!, relative[..slash]) { RequestedEntryName = relative[(slash + 1)..] };
                        }
                        return new RequestedArchive(compressed, relative);
                    }
                    catch { compressed.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
                }
                if (Directory.Exists(parent)) break;
                parent = System.IO.Path.GetDirectoryName(parent);
            }
        }
        if (ImageFormats.IsImage(path))
        {
            if (!File.Exists(path)) throw new FileNotFoundException("图片不存在。", path);
            var directory = System.IO.Path.GetDirectoryName(path)!;
            return new FolderArchive(directory) { RequestedEntryName = System.IO.Path.GetFileName(path), IsRootShortcut = new DirectoryInfo(directory).LinkTarget is not null };
        }
        if (Directory.Exists(path)) return new FolderArchive(path) { IsRootShortcut = new DirectoryInfo(path).LinkTarget is not null };
        if (!File.Exists(path)) throw new FileNotFoundException("来源不存在。", path);
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"(?:\.part\d+\.rar|\.r\d{2}|\.\d{3})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new NotSupportedException("分卷归档尚未迁移，请使用完整的单文件归档。");
        if (PlaylistSourceTools.IsPlaylist(path)) return new PlaylistArchive(path, this) { IsRootShortcut = new FileInfo(path).LinkTarget is not null };
        if (ArchiveFormats.IsCompressedArchive(path)) return new CompressedArchive(path) { IsRootShortcut = new FileInfo(path).LinkTarget is not null };
        throw new NotSupportedException("支持目录、图片、ZIP/CBZ、RAR/CBR、7z 和播放列表；其他来源尚未迁移。");
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
public sealed partial class FolderArchive(string path) : Archive(path)
{
    public override bool IsDirectory => true;
    /// <summary>有界后台枚举，最多预排两批；已知图片先产出，同一文件在后续枚举中跳过。</summary>
    /// <param name="token">读取、排队、隐藏/切书/关闭时的取消。</param>
    /// <returns>首批不等待全目录扫描；每批最多128项，所有ID在本次枚举内唯一。</returns>
    public override async IAsyncEnumerable<IReadOnlyList<ArchiveEntry>> EnumerateEntryBatchesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var producerToken = lifetime.Token;
        var channel = System.Threading.Channels.Channel.CreateBounded<IReadOnlyList<ArchiveEntry>>(2);
        var producer = ProduceAsync();
        try
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(token).ConfigureAwait(false)) yield return batch;
            await producer.ConfigureAwait(false);
        }
        finally { lifetime.Cancel(); await producer.ConfigureAwait(false); }

        // SourceIo持有槽直到真正退出；超时只结束消费者，不能提前释放仍阻塞的NAS枚举槽。
        async Task ProduceAsync()
        {
            try
            {
                await SourceIo.RunAsync(() =>
                {
                    var batch = new List<ArchiveEntry>(128); int id = 0;
                    if (RequestedEntryName is { } requested)
                    {
                        var file = new FileInfo(System.IO.Path.Combine(Path, requested));
                        if (file.Exists) { Add(file); Flush(); }
                    }
                    foreach (var info in new DirectoryInfo(Path).EnumerateFileSystemInfos())
                    {
                        producerToken.ThrowIfCancellationRequested();
                        if (info.Name.StartsWith('.') || info.Name == RequestedEntryName) continue;
                        Add(info); if (batch.Count == 128) Flush();
                    }
                    Flush(); return true;

                    void Add(FileSystemInfo info) => batch.Add(new(this) { Id = id++, RawEntryName = info.Name,
                        FilePath = info.FullName, IsDirectory = info is DirectoryInfo, IsShortcut = info.LinkTarget is not null,
                        Length = info is FileInfo value ? value.Length : -1, LastWriteTime = info.LastWriteTime });
                    void Flush()
                    {
                        if (batch.Count == 0) return;
                        channel.Writer.WriteAsync(batch.ToArray(), producerToken).AsTask().GetAwaiter().GetResult(); batch.Clear();
                    }
                }, producerToken).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex) { channel.Writer.TryComplete(ex); }
        }
    }
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
                IsShortcut = info.LinkTarget is not null,
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
public sealed partial class CompressedArchive : Archive
{
    private IArchive _archive;
    private readonly SemaphoreSlim _gate = new(1);
    private List<IArchiveEntry> _entries;
    private readonly Dictionary<int, (string Path, long Length, long Used)> _solidFiles = [];
    private string? _cacheDirectory;
    private long _cacheClock;
    private const long DiskBudget = 2L * 1024 * 1024 * 1024;
    public long CachedBytes => _solidFiles.Values.Sum(e => e.Length);
    // 7z 的块读取器也持有解码状态，统一采用独立顺序抽取和请求结果缓存。
    private bool RequiresSequential => _archive.IsSolid || _archive.Type == SharpCompress.Common.ArchiveType.SevenZip;
    /// <summary>目录可为显式条目或图片路径中的隐式目录，严格分隔边界不误匹配同前缀。</summary>
    public bool ContainsDirectory(string relative)
    {
        var path = relative.TrimEnd('/') + "/";
        return _entries.Where((_, i) => !_deletedIds.Contains(i)).Any(e => e.Key?.Replace('\\', '/').StartsWith(path, StringComparison.Ordinal) == true
            || e.IsDirectory && e.Key?.Replace('\\', '/').TrimEnd('/') == relative.TrimEnd('/'));
    }
    /// <summary>精确内部文件定位；不存在或嵌套来源不能退回根归档冒充打开成功。</summary>
    public bool ContainsFile(string relative) => _entries.Where((_, i) => !_deletedIds.Contains(i)).Any(e => !e.IsDirectory && e.Key?.Replace('\\', '/') == relative);
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
            if (_deletedIds.Contains(id)) continue;
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
                _cacheDirectory ??= System.IO.Path.Combine(ArchiveFactory.TemporaryDirectory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_cacheDirectory);
                TrimSolidCache(Math.Max(0, entry.Length));
                cachedPath = System.IO.Path.Combine(_cacheDirectory, Guid.NewGuid().ToString("N"));
                output = new FileStream(cachedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
            }
            else if (entry.Length <= 32L * 1024 * 1024) output = new MemoryStream();
            else
            {
                var directory = ArchiveFactory.TemporaryDirectory; Directory.CreateDirectory(directory);
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

/// <summary>显式图片定位包装；来源本身和读取条目仍属于唯一真实归档。</summary>
internal sealed class RequestedArchive : Archive
{
    private readonly Archive _source;
    public RequestedArchive(Archive source, string entry) : base(source.Path) { _source = source; RequestedEntryName = entry; }
    public override string RootArchivePath => _source.RootArchivePath;
    /// <summary>显式页面定位不改变当前书籍；复制仍引用原来源根条目。</summary>
    public override ArchiveEntry CreateBookEntry() => _source.CreateBookEntry();
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => _source.GetEntriesAsync(token);
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => _source.OpenEntryAsync(entry, token);
    public override async ValueTask DisposeAsync() { await _source.DisposeAsync(); IsDisposed = true; }
}

/// <summary>原包内目录逻辑来源；只过滤原条目，不解压为文件夹，拥有底层归档。</summary>
internal sealed class ArchiveDirectory(Archive source, string path, string directory) : Archive(path)
{
    private Dictionary<int, ArchiveEntry> _entries = [];
    public override string RootArchivePath => source.RootArchivePath;
    /// <summary>保留包内目录的原条目归属，复制策略不能误取当前图片或把逻辑地址当作实体目录。</summary>
    public override ArchiveEntry CreateBookEntry() => new(source) { RawEntryName = directory.TrimEnd('/'), IsDirectory = true };
    /// <summary>返回目录内相对名称，并保留原ID映射及流所有权。</summary>
    public override async Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var prefix = directory.TrimEnd('/') + "/";
        var entries = await source.GetEntriesAsync(token); var result = new List<ArchiveEntry>(); var mapping = new Dictionary<int, ArchiveEntry>();
        token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (var entry in entries.Where(e => e.EntryName.StartsWith(prefix, StringComparison.Ordinal) && e.EntryName != prefix))
        {
            token.ThrowIfCancellationRequested(); mapping[entry.Id] = entry;
            result.Add(new(this) { Id = entry.Id, RawEntryName = entry.EntryName[prefix.Length..], IsDirectory = entry.IsDirectory,
                Length = entry.Length, LastWriteTime = entry.LastWriteTime });
        }
        // 完整快照一次替换；封面探测与正文读取并行时不能看到被Clear了一半的映射。
        _entries = mapping; return result;
    }
    /// <summary>请求级读取转交物理来源；不将相对逻辑名称误用于固实Reader定位。</summary>
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => _entries.TryGetValue(entry.Id, out var original)
        ? source.OpenEntryAsync(original, token) : throw new InvalidOperationException("归档目录条目尚未建立索引。");
    private ArchiveEntry Original(ArchiveEntry entry) => _entries.TryGetValue(entry.Id, out var original) ? original
        : new(source) { Id = entry.Id, RawEntryName = directory.TrimEnd('/') + "/" + entry.EntryName, IsDirectory = entry.IsDirectory };
    public override bool CanDelete(IReadOnlyList<ArchiveEntry> entries) => !IsDisposed && entries.All(e => ReferenceEquals(e.Archive, this)) && source.CanDelete(entries.Select(Original).ToArray());
    public override async Task<PageDeleteResult> DeleteAsync(IReadOnlyList<ArchiveEntry> entries, IPlatformService platform, CancellationToken token)
    {
        var result = await source.DeleteAsync(entries.Select(Original).ToArray(), platform, token);
        var prefix = directory.TrimEnd('/') + "/";
        var removed = result.Removed.Where(e => e.EntryName.StartsWith(prefix, StringComparison.Ordinal)).Select(e => new ArchiveEntry(this)
            { Id = e.Id, RawEntryName = e.EntryName[prefix.Length..], IsDirectory = e.IsDirectory }).ToArray();
        return new(removed, result.Error);
    }
    /// <summary>关闭等到底层读取完成，不提前释放固实流。</summary>
    public override async ValueTask DisposeAsync() { await source.DisposeAsync(); IsDisposed = true; _entries.Clear(); }
}
