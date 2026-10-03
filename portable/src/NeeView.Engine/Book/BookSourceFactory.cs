// Copyright (c) NeeLaboratory. 原 BookSourceFactory/CreatePageCollection 和 ArchiveEntryCollection 的已支持内容分支。
namespace NeeView;

/// <summary>原页面收集模式及数值；高级媒体仍由后续后端接入。</summary>
public enum BookPageCollectMode { Image, ImageAndBook, All }
/// <summary>原归档收集范围；嵌套压缩执行仍在P5，目录递归和包内目录使用同一集合。</summary>
public enum ArchiveEntryCollectionMode { CurrentDirectory, IncludeSubDirectories, IncludeSubArchives }

/// <summary>按原模式过滤页面，保留有内容目录展平及空目录页面的判断。</summary>
public static class BookSourceFactory
{
    /// <summary>收集已支持来源，并按原Image/ImageAndBook/All生成Page；不包含界面类型。</summary>
    public static async Task<List<Page>> CreatePageCollectionAsync(ArchiveEntryCollection collection, BookPageCollectMode mode, IArchiveFactory archives, CancellationToken token, FolderConfigCollection? folders = null)
    {
        var entries = await collection.GetEntriesAsync(token);
        IEnumerable<ArchiveEntryNode> source = entries;
        if (mode == BookPageCollectMode.All || mode == BookPageCollectMode.ImageAndBook && collection.Mode != ArchiveEntryCollectionMode.CurrentDirectory)
        {
            var parents = entries.Select(e => System.IO.Path.GetDirectoryName(e.ArchiveEntry.SystemPath.TrimEnd('/'))).ToHashSet(StringComparer.Ordinal);
            source = source.Where(e => e.ArchiveEntry.IsShortcut || !parents.Contains(e.ArchiveEntry.SystemPath.TrimEnd('/')));
        }
        source = mode switch
        {
            BookPageCollectMode.Image => source.Where(e => e.ArchiveEntry.IsImage()),
            BookPageCollectMode.ImageAndBook => source.Where(e => e.ArchiveEntry.IsImage() || e.ArchiveEntry.IsBook()),
            _ => source
        };
        return source.Select(e => new Page(e.ArchiveEntry, e.EntryName, archives, folders)).ToList();
    }
}

/// <summary>原条目所属关系及根内逻辑名称，不把临时解压路径用作页面定位。</summary>
public sealed record ArchiveEntryNode(ArchiveEntry ArchiveEntry, string EntryName);

/// <summary>原根来源及递归来源的唯一所有者；关闭/失败时释放全部来源。</summary>
public sealed class ArchiveEntryCollection(Archive root, IArchiveFactory archives, bool recursive) : IAsyncDisposable
{
    private readonly List<Archive> _owned = [root];
    public ArchiveEntryCollectionMode Mode { get; } = recursive ? ArchiveEntryCollectionMode.IncludeSubArchives
        : root.IsDirectory ? ArchiveEntryCollectionMode.CurrentDirectory : Config.Current.System.ArchiveRecursiveMode;
    /// <summary>收集元数据；普通目录按原递归进入子书，符号链接不继续展开以避免环。</summary>
    public async Task<IReadOnlyList<ArchiveEntryNode>> GetEntriesAsync(CancellationToken token)
    {
        var result = new List<ArchiveEntryNode>(); await CollectAsync(root, "", result, token); return result;
    }
    /// <summary>每个条目先登记，再按原IncludeSubArchives展开；失败子书保留为可识别页面。</summary>
    private async Task CollectAsync(Archive archive, string prefix, List<ArchiveEntryNode> result, CancellationToken token)
    {
        var entries = await archive.GetEntriesAsync(token);
        if (!archive.IsDirectory && Mode == ArchiveEntryCollectionMode.CurrentDirectory)
            entries = GetCurrentDirectoryEntries(archive, entries);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var name = prefix + entry.EntryName.TrimEnd('/'); result.Add(new(entry, name));
            // 真正的嵌套归档需要专门后端，P5接入；包内目录已经由当前归档一次返回。
            if (Mode != ArchiveEntryCollectionMode.IncludeSubArchives || !archive.IsDirectory || !entry.IsBook() || entry.IsShortcut) continue;
            try
            {
                var child = await archives.OpenAsync(entry.SystemPath, token); _owned.Add(child);
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
