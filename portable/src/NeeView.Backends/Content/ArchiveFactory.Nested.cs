// Copyright (c) NeeLaboratory. 原ArchiveManager.CreateArchiveAsync(source)与ArchiveEntryUtility逐层定位适配。
using NeeView;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    internal const int MaximumNestedDepth = 16;
    /// <summary>按源条目打开子归档，保留重复名称的物理ID；父来源继续归原Book/封面请求所有。</summary>
    /// <param name="entry">实际来源条目，不能在调用中关闭其父来源。</param><param name="token">解压和临时准备取消。</param>
    /// <returns>独立子来源；调用方释放子来源，父来源不随之关闭。</returns>
    public Task<Archive> OpenAsync(ArchiveEntry entry, CancellationToken token)
    {
        entry = entry.TargetArchiveEntry;
        if (entry.FilePath is not null || entry.Archive.IsDirectory) return OpenAsync(entry.SystemPath, token);
        if (entry.IsDirectory) return Task.FromResult<Archive>(new ArchiveDirectory(entry.Archive, entry.SystemPath, entry.EntryName, false));
        if (!entry.IsBook() || !ArchiveFormats.IsPageArchive(entry.EntryName)) throw new NotSupportedException("内部条目不是已支持的子归档。");
        return OpenWithKeysAsync(keys => OpenNestedAsync(entry, false, keys, token), null, token);
    }
    /// <summary>压缩文件不是目录；不能把内部“..”规范化成归档外的真实文件。</summary>
    private static void RejectInnerParentTraversal(string path)
    {
        if (!path.Split('/').Contains("..")) return;
        // 在任何GetFullPath/Exists规范化“..”前，按原分隔位置检查真实根文件。
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            var prefix = path[..slash];
            if (ArchiveFormats.IsPageArchive(prefix) && File.Exists(prefix)
                && path[(slash + 1)..].Split('/').Contains(".."))
                throw new NotSupportedException("归档内部定位不能包含上级路径段。");
        }
    }
    /// <summary>原逐层内部定位；真实条目优先，路径边界严格，抽取名称由应用生成。</summary>
    private async Task<Archive> ResolveInnerAsync(Archive source, string relative, ArchiveKeys keys, CancellationToken token)
    {
        relative = relative.Replace((char)92, '/').TrimEnd('/');
        var entries = await source.GetEntriesAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
        if (entries.Any(e => e.EntryName.TrimEnd('/') == relative && e.IsDirectory || e.EntryName.StartsWith(relative + "/", StringComparison.Ordinal)))
            return new ArchiveDirectory(source, System.IO.Path.Combine(source.Path, relative), relative);
        var entry = entries.FirstOrDefault(e => !e.IsDirectory && e.EntryName == relative);
        if (entry is not null)
        {
            // 类型由真实来源判定：PDF虚拟PNG即使与自定义PDF后缀重合，仍是页面。
            if (entry.IsBook() && ArchiveFormats.IsPageArchive(entry.EntryName)) return await OpenNestedAsync(entry, true, keys, token).ConfigureAwait(false);
            if (Config.Current.System.ArchiveRecursiveMode == ArchiveEntryCollectionMode.CurrentDirectory && relative.Contains('/'))
            {
                var slash = relative.LastIndexOf('/');
                return new ArchiveDirectory(source, System.IO.Path.Combine(source.Path, relative[..slash]), relative[..slash]) { RequestedEntryName = relative[(slash + 1)..] };
            }
            return new RequestedArchive(source, relative);
        }
        var container = entries.Where(e => !e.IsDirectory && e.IsBook() && ArchiveFormats.IsPageArchive(e.EntryName)
            && relative.StartsWith(e.EntryName + "/", StringComparison.Ordinal)).OrderByDescending(e => e.EntryName.Length).FirstOrDefault();
        if (container is null) throw new FileNotFoundException("归档内部指定页面或目录不存在：" + relative);
        var child = await OpenNestedAsync(container, true, keys, token).ConfigureAwait(false);
        try { return await ResolveInnerAsync(child, relative[(container.EntryName.Length + 1)..], keys, token).ConfigureAwait(false); }
        catch { await child.DisposeAsync(); throw; }
    }
    /// <summary>原临时代理文件随子归档生命周期；借用入口不关闭父来源，路径入口拥有完整父链。</summary>
    private async Task<Archive> OpenNestedAsync(ArchiveEntry entry, bool ownsParent, ArchiveKeys keys, CancellationToken token)
    {
        if (entry.Archive.NestingDepth >= MaximumNestedDepth) throw new NotSupportedException($"嵌套归档超过 {MaximumNestedDepth} 层限制。");
        if (entry.EntryName.Split('/').Contains("..")) throw new NotSupportedException("归档内部定位不能包含上级路径段。");
        // 先完成来源读取，再进入临时准备槽，避免后台槽中的嵌套等待造成死锁。
        var input = await entry.Archive.OpenEntryAsync(entry, token).ConfigureAwait(false);
        return await SourceIo.RunAsync<Archive>(() =>
        {
            token.ThrowIfCancellationRequested(); var lifetime = new NestedArchiveFile(ownsParent ? entry.Archive : null);
            try
            {
                lifetime.Write(input, entry.Length, token);
                token.ThrowIfCancellationRequested();
                return CreatePageArchive(lifetime.Path, keys, token, entry.SystemPath, entry, lifetime);
            }
            catch { lifetime.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        }, token, input).ConfigureAwait(false);
    }
}

/// <summary>仅应用生成的嵌套代理文件；所有活动代理共享2GiB上限，没有第二持久化或内容身份缓存。</summary>
internal sealed class NestedArchiveFile(Archive? ownedParent) : IAsyncDisposable
{
    internal const long Budget = 2L * 1024 * 1024 * 1024;
    private static long _reserved;
    private long _bytes;
    private int _disposed;
    public string Path { get; } = System.IO.Path.Combine(ArchiveFactory.TemporaryDirectory, "nested-" + Guid.NewGuid().ToString("N"));
    internal static long ReservedBytes => Interlocked.Read(ref _reserved);
    /// <summary>流式复制并复核长度，临时文件名不使用归档内部路径；不完整结果不能成为来源。</summary>
    /// <param name="input">外层条目流，由调用方持有到真正复制结束。</param><param name="expected">原索引长度。</param>
    /// <param name="token">复制及后台超时后的取消。</param>
    internal void Write(Stream input, long expected, CancellationToken token)
    {
        if (expected > 0) Reserve(expected);
        Directory.CreateDirectory(ArchiveFactory.TemporaryDirectory);
        using var output = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, FileOptions.SequentialScan);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested(); total = checked(total + read);
            if (total > _bytes) Reserve(total - _bytes);
            output.Write(buffer, 0, read);
        }
        if (expected >= 0 && total != expected) throw new InvalidDataException("内部归档读取不完整，实际长度与索引不一致。");
        token.ThrowIfCancellationRequested();
    }
    /// <summary>跨活动子来源原子预留，超出预算明确失败；失败不扣减其他来源额度。</summary>
    internal void Reserve(long bytes)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (true)
        {
            var before = Interlocked.Read(ref _reserved);
            if (bytes > Budget - before) throw new NotSupportedException("活动嵌套归档超过 2 GiB 临时代理预算。");
            if (Interlocked.CompareExchange(ref _reserved, before + bytes, before) == before) { _bytes += bytes; return; }
        }
    }
    /// <summary>子归档关闭之后删除自身代理，再释放额度和仅路径入口拥有的父链。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { if (File.Exists(Path)) File.Delete(Path); Interlocked.Add(ref _reserved, -_bytes); _bytes = 0; }
        catch { Volatile.Write(ref _disposed, 0); throw; }
        finally { if (ownedParent is not null) await ownedParent.DisposeAsync(); }
    }
}
