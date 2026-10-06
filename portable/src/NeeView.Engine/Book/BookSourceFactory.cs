// Copyright (c) NeeLaboratory. 原 BookSourceFactory/CreatePageCollection 和 ArchiveEntryCollection 的已支持内容分支。
namespace NeeView;

/// <summary>原页面收集模式及数值；高级媒体仍由后续后端接入。</summary>
public enum BookPageCollectMode { Image, ImageAndBook, All }
/// <summary>原归档收集范围；目录、包内目录与嵌套压缩共用同一集合。</summary>
public enum ArchiveEntryCollectionMode { CurrentDirectory, IncludeSubDirectories, IncludeSubArchives }

/// <summary>按原模式过滤页面，保留有内容目录展平及空目录页面的判断。</summary>
public static class BookSourceFactory
{
    /// <summary>原ValidatePageSortMode：列表允许Entry类别，普通书籍保持文件名回退。</summary>
    /// <param name="mode">阅读配置中的原排序。</param><param name="source">真实书籍来源。</param><returns>原来源支持的有效排序。</returns>
    public static PageSortMode ValidatePageSortMode(PageSortMode mode, Archive source) => !source.IsPlaylist && mode.IsEntryCategory()
        ? mode.IsDescending() ? PageSortMode.FileNameDescending : PageSortMode.FileName : mode;
    /// <summary>直接目录分批构造原Page；递归展平和归档继续完整过滤，避免过早发布会消失的目录项。</summary>
    /// <param name="collection">唯一来源所有者。</param><param name="mode">原过滤模式。</param>
    /// <param name="archives">原来源工厂。</param><param name="token">逐条取消。</param>
    /// <param name="folders">每目录配置。</param><returns>同一来源中只构造一次的原Page批次。</returns>
    public static async IAsyncEnumerable<List<Page>> CreatePageBatchesAsync(ArchiveEntryCollection collection, BookPageCollectMode mode,
        IArchiveFactory archives, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, FolderConfigCollection? folders = null)
    {
        if (!collection.Root.IsDirectory || collection.Mode != ArchiveEntryCollectionMode.CurrentDirectory)
        { yield return await CreatePageCollectionAsync(collection, mode, archives, token, folders).ConfigureAwait(false); yield break; }
        await foreach (var batch in collection.Root.EnumerateEntryBatchesAsync(token).ConfigureAwait(false))
        {
            var pages = await Task.Run(() =>
            {
                var result = new List<Page>();
                foreach (var entry in batch)
                {
                    token.ThrowIfCancellationRequested();
                    if (mode == BookPageCollectMode.Image && !entry.IsImage() || mode == BookPageCollectMode.ImageAndBook && !entry.IsImage() && !entry.IsBook()) continue;
                    result.Add(new(entry, entry.EntryName, archives, folders));
                }
                return result;
            }, token).ConfigureAwait(false);
            if (pages.Count > 0) yield return pages;
        }
    }
    /// <summary>收集已支持来源，并按原Image/ImageAndBook/All生成Page；不包含界面类型。</summary>
    /// <param name="collection">拥有根/子来源的原集合，失败后由调用方释放。</param>
    /// <param name="mode">原页面收集模式，数值和过滤顺序保持。</param>
    /// <param name="archives">供后续目录/归档封面使用的既有来源工厂。</param>
    /// <param name="token">枚举、过滤和逐页构造的取消令牌。</param>
    /// <param name="folders">原每目录配置，构造期间不修改。</param>
    /// <returns>未排序、未提交的原Page集合；不发布部分书籍。</returns>
    public static async Task<List<Page>> CreatePageCollectionAsync(ArchiveEntryCollection collection, BookPageCollectMode mode, IArchiveFactory archives, CancellationToken token, FolderConfigCollection? folders = null)
    {
        var entries = await collection.GetEntriesAsync(token).ConfigureAwait(false);
        // 过滤/构造万项Page是CPU工作，不能在来源返回后重新占住UI线程。
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            HashSet<string>? parents = null;
            if (mode == BookPageCollectMode.All || mode == BookPageCollectMode.ImageAndBook && collection.Mode != ArchiveEntryCollectionMode.CurrentDirectory)
            {
                parents = new(StringComparer.Ordinal);
                foreach (var entry in entries) { token.ThrowIfCancellationRequested(); parents.Add(System.IO.Path.GetDirectoryName(entry.ArchiveEntry.SystemPath.TrimEnd('/'))!); }
            }
            var pages = new List<Page>(entries.Count);
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested(); var value = entry.ArchiveEntry;
                if (parents is not null && !value.IsShortcut && parents.Contains(value.SystemPath.TrimEnd('/'))) continue;
                if (mode == BookPageCollectMode.Image && !value.IsImage()
                    || mode == BookPageCollectMode.ImageAndBook && !value.IsImage() && !value.IsBook()) continue;
                pages.Add(new(value, entry.EntryName, archives, folders));
            }
            return pages;
        }, token).ConfigureAwait(false);
    }
}

/// <summary>原条目所属关系及根内逻辑名称，不把临时解压路径用作页面定位。</summary>
public sealed record ArchiveEntryNode(ArchiveEntry ArchiveEntry, string EntryName);

/// <summary>原根来源及递归来源的唯一所有者；关闭/失败时释放全部来源。</summary>
public sealed class ArchiveEntryCollection(Archive root, IArchiveFactory archives, bool recursive) : IAsyncDisposable
{
    public Archive Root => root;
    private readonly List<Archive> _owned = [root];
    public ArchiveEntryCollectionMode Mode { get; } = recursive ? ArchiveEntryCollectionMode.IncludeSubArchives
        : root.IsDirectory || root.IsPlaylist ? ArchiveEntryCollectionMode.CurrentDirectory : Config.Current.System.ArchiveRecursiveMode;
    /// <summary>收集元数据；普通目录按原递归进入子书，符号链接不继续展开以避免环。</summary>
    /// <param name="token">完整收集的取消令牌，取消后已拥有来源仍由本集合关闭。</param>
    /// <returns>完整条目快照；本方法不把部分索引发布给阅读端。</returns>
    public async Task<IReadOnlyList<ArchiveEntryNode>> GetEntriesAsync(CancellationToken token)
    {
        // 同步完成的归档也可能包含万项；整个元数据收集循环放在后台，所有权仍归本集合。
        return await Task.Run(async () =>
        {
            var result = new List<ArchiveEntryNode>(); await CollectAsync(root, "", result, token); return result;
        }, token).ConfigureAwait(false);
    }
    /// <summary>每个条目先登记，再按原IncludeSubArchives展开；失败子书保留为可识别页面。</summary>
    private async Task CollectAsync(Archive archive, string prefix, List<ArchiveEntryNode> result, CancellationToken token)
    {
        var entries = await archive.GetEntriesAsync(token);
        if (!archive.IsDirectory && !archive.IsPlaylist && Mode == ArchiveEntryCollectionMode.CurrentDirectory)
            entries = GetCurrentDirectoryEntries(archive, entries);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var name = prefix + entry.EntryName.TrimEnd('/'); result.Add(new(entry, name));
            // 包内目录已由当前来源一次返回；只为真正的压缩文件新增子来源，不能重复展开目录。
            if (Mode != ArchiveEntryCollectionMode.IncludeSubArchives || !entry.IsBook() || entry.IsShortcut
                || !archive.IsDirectory && !archive.IsPlaylist && (entry.IsDirectory || !ArchiveFormats.IsPageArchive(entry.EntryName))) continue;
            try
            {
                var child = await archives.OpenAsync(entry, token); _owned.Add(child);
                await CollectAsync(child, name + "/", result, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("子书读取：" + ex.Message); }
        }
    }
    /// <summary>CurrentDirectory只返回直接内容；为不显式存储目录项的ZIP补齐逻辑目录。</summary>
    private static IReadOnlyList<ArchiveEntry> GetCurrentDirectoryEntries(Archive archive, IReadOnlyList<ArchiveEntry> entries)
    {
        var result = entries.Where(e => !e.EntryName.TrimEnd('/').Contains('/')).ToList();
        var names = result.Select(e => e.EntryName.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        int id = -1;
        foreach (var entry in entries)
        {
            int slash = entry.EntryName.IndexOf('/'); if (slash <= 0) continue;
            var name = entry.EntryName[..slash];
            if (names.Add(name)) result.Add(new(archive) { Id = id--, RawEntryName = name, IsDirectory = true, Length = -1 });
        }
        return result;
    }
    /// <summary>按反向所有权顺序释放，当前请求由具体来源的Dispose等待完成。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var archive in _owned.AsEnumerable().Reverse()) await archive.DisposeAsync(); _owned.Clear();
    }
}
