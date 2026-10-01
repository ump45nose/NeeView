using System.IO.Compression;
using System.Threading.Channels;
using NeeView.Application;
using NeeView.Core;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace NeeView.Content;

/// <summary>有界 I/O 队列；NAS 超时后保留调用的释放责任。</summary>
internal static class SourceIo
{
    private static readonly SemaphoreSlim Slots = new(2);
    /// <summary>输入同步文件工作，后台执行；等待超时不无限创建工作线程。</summary>
    public static async Task<T> RunAsync<T>(Func<T> action, CancellationToken token)
    {
        if (!await Slots.WaitAsync(TimeSpan.FromSeconds(15), token)) throw new TimeoutException("文件系统队列暂不可访问。");
        var task = Task.Run(() => { try { return action(); } finally { Slots.Release(); } }, CancellationToken.None);
        try { return await task.WaitAsync(TimeSpan.FromSeconds(15), token); }
        catch
        {
            // 文件系统调用可能不可中断；晚到流仍须释放，异常必须被观察。
            _ = task.ContinueWith(done =>
            {
                if (done.IsCompletedSuccessfully && done.Result is IDisposable disposable) disposable.Dispose();
                _ = done.Exception;
            }, TaskScheduler.Default);
            throw;
        }
    }
    /// <summary>产生临时文件的工作不能遗弃；取消后等待其清理完成才归还来源互斥。</summary>
    public static async Task<T> RunOwnedAsync<T>(Func<T> action, CancellationToken token)
    {
        if (!await Slots.WaitAsync(TimeSpan.FromSeconds(15), token)) throw new TimeoutException("解压队列暂不可访问。");
        return await Task.Run(() => { try { return action(); } finally { Slots.Release(); } }, CancellationToken.None);
    }
}
public sealed class ContentSourceFactory(IIdentityRegistry registry, string cacheRoot) : IContentSourceFactory
{
    private static readonly HashSet<string> Archives = new([".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7"], StringComparer.OrdinalIgnoreCase);
    /// <summary>输入原始路径，返回目录或只读归档来源；单图使用所在目录。</summary>
    public async Task<IContentSource> OpenAsync(OpenRequest request, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(request.Path);
        var exists = await SourceIo.RunAsync(() => (Directory.Exists(path), File.Exists(path)), cancellationToken);
        if (exists.Item1) return new DirectorySource(path, null, registry);
        if (!exists.Item2) throw new ReaderException(FailureKind.NotFound, $"来源不存在：{path}");
        if (Archives.Contains(Path.GetExtension(path)))
            return new ArchiveSource(path, request.Entry, registry, cacheRoot);
        if (DirectorySource.IsImage(path)) return new DirectorySource(Path.GetDirectoryName(path)!, path, registry);
        throw new ReaderException(FailureKind.Unsupported, "首版支持图片、目录及 ZIP/RAR/7z；PDF、媒体、嵌套归档尚不支持。");
    }
}
public sealed class DirectorySource(string directory, string? requested, IIdentityRegistry registry) : IProgressiveContentSource
{
    private bool _disposed;
    public SourceLocator Locator { get; } = new(directory);
    public SourceCapabilities Capabilities { get; } = new(true, AccessCost.Random, false);
    private static readonly HashSet<string> Images = new([".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif"], StringComparer.OrdinalIgnoreCase);
    /// <summary>按首版格式声明判断图片文件，不读取内容。</summary>
    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(path));
    /// <summary>完整索引用于刷新；打开流程使用批次接口先显示目标图片。</summary>
    public async Task<SourceIndex> IndexAsync(CancellationToken cancellationToken)
    {
        SourceIndex? result = null;
        await foreach (var batch in IndexBatchesAsync(cancellationToken)) result = batch;
        return result!;
    }
    /// <summary>每批最多128条；已知单图先登记并发布，枚举始终在有界后台 I/O 中执行。</summary>
    public async IAsyncEnumerable<SourceIndex> IndexBatchesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var book = await registry.GetBookAsync(Locator, token);
        var pages = new List<PageDescriptor>(); ContentId? selected = null;
        if (requested is not null)
        {
            var info = await SourceIo.RunAsync(() =>
            { var file = new FileInfo(requested); return (file.Exists, file.FullName, file.Name, Length: file.Exists ? file.Length : 0, Stamp: file.Exists ? file.LastWriteTimeUtc.Ticks : 0); }, token);
            if (info.Exists)
            {
                selected = await registry.GetContentAsync(new(info.FullName), token);
                pages.Add(new(selected.Value, info.Name, new(info.FullName), new(info.Length, info.Stamp), -1));
                yield return new(book, Locator, pages.ToArray(), selected, Capabilities);
            }
        }
        var order = 0;
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var batches = Channel.CreateBounded<(string Path, string Name, long Length, long Stamp)[]>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true });
        _ = ProduceAsync(); // 生产者独占迭代器；超时返回后不并发释放仍在系统调用中的句柄。
        try
        {
            while (true)
            {
                (string Path, string Name, long Length, long Stamp)[] batch = []; var completed = false;
                try { batch = await batches.Reader.ReadAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), token); }
                catch (ChannelClosedException)
                {
                    await batches.Reader.Completion; // 损坏、权限等生产者错误保持真实类别。
                    completed = true;
                }
                if (completed)
                {
                    var final = pages.OrderBy(p => p.Name, NaturalNameComparer.Instance).Select((p, i) => p with { EntryIndex = i }).ToArray();
                    yield return new(book, Locator, final, selected, Capabilities); yield break;
                }
                token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
                foreach (var entry in batch)
                {
                    var locator = new SourceLocator(entry.Path); var id = await registry.GetContentAsync(locator, token);
                    var page = new PageDescriptor(id, entry.Name, locator, new(entry.Length, entry.Stamp), order++);
                    var existing = pages.FindIndex(p => p.Id == id);
                    if (existing >= 0) pages[existing] = page; else pages.Add(page);
                }
                yield return new(book, Locator, pages.ToArray(), selected, Capabilities);
            }
        }
        finally { producerCancellation.Cancel(); }
        // 所有文件元数据在后台读取；有限通道控制预枚举量，晚到调用自行释放迭代器。
        async Task ProduceAsync()
        {
            var producerToken = producerCancellation.Token;
            try
            {
                await SourceIo.RunOwnedAsync(() =>
                {
                    using var iterator = Directory.EnumerateFiles(directory).GetEnumerator();
                    var entries = new List<(string, string, long, long)>();
                    while (iterator.MoveNext())
                    {
                        producerToken.ThrowIfCancellationRequested();
                        if (!IsImage(iterator.Current)) continue;
                        var file = new FileInfo(iterator.Current); entries.Add((file.FullName, file.Name, file.Length, file.LastWriteTimeUtc.Ticks));
                        if (entries.Count < 128) continue;
                        batches.Writer.WriteAsync(entries.ToArray(), producerToken).AsTask().GetAwaiter().GetResult(); entries.Clear();
                    }
                    if (entries.Count > 0) batches.Writer.WriteAsync(entries.ToArray(), producerToken).AsTask().GetAwaiter().GetResult();
                    return true;
                }, producerToken);
                batches.Writer.TryComplete();
            }
            catch (Exception error) { batches.Writer.TryComplete(error); }
        }
    }
    /// <summary>返回请求独占文件流；允许应用随后移动或删除源文件。</summary>
    public async Task<Stream> OpenReadAsync(PageDescriptor page, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await SourceIo.RunAsync<Stream>(() => new FileStream(page.Locator.Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan), cancellationToken);
    }
    public ValueTask DisposeAsync() { _disposed = true; return ValueTask.CompletedTask; }
}

/// <summary>归档索引与受限解压缓存；临时文件只采用随机应用名称。</summary>
public sealed class ArchiveSource : IContentSource
{
    private readonly IIdentityRegistry _registry;
    private readonly string? _requested;
    private readonly string _cache;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, string> _extracted = new(StringComparer.Ordinal);
    private bool _disposed;
    private const long DiskBudget = 2L * 1024 * 1024 * 1024;
    public SourceLocator Locator { get; }
    public SourceCapabilities Capabilities { get; private set; }
    public ArchiveSource(string path, string? requested, IIdentityRegistry registry, string cacheRoot)
    {
        Locator = new(path); _requested = requested; _registry = registry;
        _cache = Path.Combine(cacheRoot, "archive-" + Guid.NewGuid().ToString("N"));
        Capabilities = new(false, Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase) ? AccessCost.Block : AccessCost.Random, true);
    }
    /// <summary>只读索引归档；密码、分卷和嵌套归档给出明确提示。</summary>
    public async Task<SourceIndex> IndexAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var metadata = await SourceIo.RunAsync(() =>
        {
            using var archive = ArchiveFactory.OpenArchive(Locator.Path);
            if (!archive.IsComplete || System.Text.RegularExpressions.Regex.IsMatch(Locator.Path, @"\.part[0-9]+\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || File.Exists(Path.ChangeExtension(Locator.Path, ".r00")) || File.Exists(Path.ChangeExtension(Locator.Path, ".z01")))
                throw new ReaderException(FailureKind.Unsupported, "分卷压缩包暂不支持，请先在外部合并或解压。");
            if (archive.IsSolid) Capabilities = Capabilities with { AccessCost = AccessCost.Sequential };
            var values = new List<(string Key, long Size)>();
            var nested = false;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsEncrypted) throw new ReaderException(FailureKind.Password, "此压缩包需要密码，首版尚不支持。");
                if (!entry.IsDirectory && entry.Key is { } key && DirectorySource.IsImage(key)) values.Add((key, entry.Size));
                else if (!entry.IsDirectory && entry.Key is { } archiveKey && new[] { ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7" }.Contains(Path.GetExtension(archiveKey), StringComparer.OrdinalIgnoreCase)) nested = true;
            }
            if (values.Count == 0 && nested) throw new ReaderException(FailureKind.Unsupported, "此来源仅包含嵌套压缩包，首版暂不支持递归打开。");
            return values;
        }, cancellationToken);
        var stamp = await SourceIo.RunAsync(() => new FileInfo(Locator.Path).LastWriteTimeUtc.Ticks, cancellationToken);
        var pages = new List<PageDescriptor>();
        foreach (var entry in metadata)
        {
            var locator = Locator with { Entry = entry.Key };
            pages.Add(new(await _registry.GetContentAsync(locator, cancellationToken), entry.Key, locator, new(entry.Size, stamp), pages.Count));
        }
        return new(await _registry.GetBookAsync(Locator, cancellationToken), Locator, pages,
            pages.FirstOrDefault(p => p.Locator.Entry == _requested)?.Id, Capabilities);
    }
    /// <summary>按需解压目标条目；固实归档通过顺序 Reader 扫描。</summary>
    public async Task<Stream> OpenReadAsync(PageDescriptor page, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = page.Locator.Entry ?? throw new InvalidDataException("缺少归档条目定位。");
            if (!_extracted.TryGetValue(key, out var cached) || !File.Exists(cached))
            {
                Directory.CreateDirectory(_cache);
                if (page.Version.Length < 0 || page.Version.Length > DiskBudget) throw new ReaderException(FailureKind.Unsupported, "条目大小不可信或超过 2 GiB 解压预算。");
                cached = Path.Combine(_cache, Guid.NewGuid().ToString("N"));
                ArchiveCacheBudget.Reserve(cached, page.Version.Length);
                Directory.CreateDirectory(_cache);
                var target = cached;
                try
                {
                    await SourceIo.RunOwnedAsync(() =>
                    {
                        using var archive = ArchiveFactory.OpenArchive(Locator.Path);
                        Stream? source = null;
                        using var reader = archive.IsSolid ? archive.ExtractAllEntries() : null;
                        if (reader is not null)
                        {
                            while (reader.MoveToNextEntry())
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (reader.Entry.Key == key) { source = reader.OpenEntryStream(); break; }
                            }
                        }
                        else source = archive.Entries.FirstOrDefault(e => e.Key == key)?.OpenEntryStream();
                        if (source is null) throw new InvalidDataException("归档条目不存在。");
                        using (source)
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                        {
                            var buffer = new byte[64 * 1024]; long written = 0; int count;
                            while ((count = source.Read(buffer)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested(); written += count;
                                if (written > page.Version.Length || written > DiskBudget) throw new InvalidDataException("解压输出超过磁盘总预算。");
                                output.Write(buffer, 0, count);
                            }
                        }
                        return true;
                    }, cancellationToken);
                    _extracted[key] = cached;
                }
                catch { ArchiveCacheBudget.Retire(cached); throw; }
            }
            return ArchiveCacheBudget.Open(cached);
        }
        finally { _gate.Release(); }
    }
    /// <summary>等待正在执行的解压工作完成，再删除本来源缓存。</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _disposed = true; foreach (var path in _extracted.Values) ArchiveCacheBudget.Retire(path); _extracted.Clear(); if (Directory.Exists(_cache) && !Directory.EnumerateFileSystemEntries(_cache).Any()) Directory.Delete(_cache); }
        finally { _gate.Release(); }
    }
}
