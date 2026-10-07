// Copyright (c) NeeLaboratory. 原PlaylistArchive读取/代理/失败跳过，基线c5c398d89。
using NeeView;
namespace NeeView.Backends;

/// <summary>原.nvpls作为一本书的来源；不读取或改变全局PlaylistHub。</summary>
public sealed partial class PlaylistArchive(string path, IArchiveFactory archives) : Archive(path)
{
    public override string BackendName => "NeeView 播放列表";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, Archive> _owned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ArchiveEntry>> _indexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchiveEntry> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _physicalIds = new(StringComparer.Ordinal);
    private IReadOnlyList<ArchiveEntry>? _entries;
    private byte[]? _sourceBytes;
    private PlaylistSource? _source;
    private readonly Dictionary<ArchiveEntry, PlaylistSourceItem> _sourceItems = [];
    public override bool IsPlaylist => true;
    public int SkippedEntryCount { get; private set; }
    /// <summary>严格按源顺序构造代理；缺失/不支持项按原实现跳过，成功Id连续。</summary>
    /// <param name="token">解析和各来源读取可取消。</param><returns>唯一稳定代理索引，重复Path仍有独立代理。</returns>
    public override async Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_entries is not null) return _entries;
            var bytes = await SourceIo.RunAsync(() =>
            {
                using var input = File.OpenRead(Path); using var output = new MemoryStream();
                byte[] buffer = new byte[81920]; int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + read > 8L * 1024 * 1024) throw new NotSupportedException("播放列表超过8MiB读取预算。");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }, token).ConfigureAwait(false);
            var playlist = PlaylistSourceTools.Deserialize(bytes); _sourceBytes = bytes; _source = playlist;
            if (playlist.Items.Count > 10000) throw new NotSupportedException("播放列表超过一万项来源预算。");
            var result = new List<ArchiveEntry>(); SkippedEntryCount = 0;
            foreach (var item in playlist.Items)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var target = System.IO.Path.GetFullPath(item.Path, System.IO.Path.GetDirectoryName(Path)!);
                    var inner = await ResolveAsync(target, token).ConfigureAwait(false);
                    var proxy = new PlaylistArchiveEntry(this, inner, result.Count, item.Name);
                    result.Add(proxy); _sourceItems.Add(proxy, item);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SkippedEntryCount++; System.Diagnostics.Trace.WriteLine("列表来源条目：" + ex.Message); }
            }
            token.ThrowIfCancellationRequested(); return _entries = result;
        }
        finally { _gate.Release(); }
    }
    /// <summary>实际条目按定位复用；不枚举实体图片所在整目录，不沿列表别名解压。</summary>
    private async Task<ArchiveEntry> ResolveAsync(string path, CancellationToken token)
    {
        if (_resolved.TryGetValue(path, out var resolved)) return resolved;
        var info = await archives.GetFileMetadataAsync(path, token).ConfigureAwait(false);
        if (info is not null)
        {
            var parent = System.IO.Path.GetDirectoryName(path) ?? path;
            if (!_owned.TryGetValue(parent, out var owner)) _owned.Add(parent, owner = new FolderArchive(parent));
            _physicalIds.TryGetValue(parent, out int id); _physicalIds[parent] = id + 1;
            return _resolved[path] = new(owner) { Id = id, RawEntryName = info.Name, FilePath = path,
                IsDirectory = info.IsDirectory, IsShortcut = info.IsSymbolicLink || PlaylistSourceTools.IsPlaylist(path),
                Length = info.Length, LastWriteTime = info.LastWriteTime };
        }
        // 已拥有的归档先精确匹配，多个包内图片不重复持有原生归档/固实缓存。
        foreach (var owner in _owned.Values.Where(a => !a.IsDirectory && path.StartsWith(a.Path + "/", StringComparison.Ordinal)).OrderByDescending(a => a.Path.Length))
        {
            var entries = await IndexAsync(owner, token).ConfigureAwait(false);
            if (entries.FirstOrDefault(e => e.SystemPath == path) is { } entry) return _resolved[path] = entry;
        }
        var opened = await archives.OpenAsync(path, token).ConfigureAwait(false);
        var requested = opened.RequestedEntryName;
        if (_owned.TryGetValue(opened.Path, out var existing)) { await opened.DisposeAsync(); opened = existing; }
        else _owned.Add(opened.Path, opened);
        if (requested is not null)
        {
            var entry = (await IndexAsync(opened, token).ConfigureAwait(false)).FirstOrDefault(e => e.EntryName == requested)
                ?? throw new FileNotFoundException("列表指定归档页面已不存在。", path);
            return _resolved[path] = entry;
        }
        // 工厂已核实的包内目录仅作为书籍项，不把虚拟定位标为FilePath。
        if (opened.Path == path) return _resolved[path] = new(opened) { RawEntryName = "", IsDirectory = true, Length = -1 };
        throw new FileNotFoundException("列表来源无法定位。", path);
    }
    /// <summary>所属来源的只读索引每次列表生命周期最多建立一次。</summary>
    private async Task<IReadOnlyList<ArchiveEntry>> IndexAsync(Archive source, CancellationToken token)
    {
        if (!_indexes.TryGetValue(source.Path, out var entries)) _indexes[source.Path] = entries = await source.GetEntriesAsync(token).ConfigureAwait(false);
        return entries;
    }
    /// <summary>代理读取转交实际来源；流所有权仍由解码请求持有。</summary>
    public override async Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (entry is not PlaylistArchiveEntry proxy || !ReferenceEquals(entry.Archive, this)) throw new ArgumentException("条目不属于此列表。", nameof(entry));
            return await proxy.InnerEntry.Archive.OpenEntryAsync(proxy.InnerEntry, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>关书先等待实际读取，再释放所有所属来源；.nvpls本身由用户或进程临时服务拥有。</summary>
    public override async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var source in _owned.Values.Reverse()) await source.DisposeAsync();
            _owned.Clear(); _indexes.Clear(); _resolved.Clear(); _entries = null; IsDisposed = true;
        }
        finally { _gate.Release(); }
    }
}
