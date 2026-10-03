// Copyright (c) NeeLaboratory. 原 ArchivePageUtility/ArchiveEntryUtility.CreateFirstImageArchiveEntryAsync 的已支持分支。
using System.Text.RegularExpressions;
namespace NeeView;

/// <summary>原书籍页按需封面选择；读取者拥有请求来源，不保留全目录句柄。</summary>
public static class ArchivePageUtility
{
    /// <summary>历史/书架/书签沿原路径取封面；来源仅属于本次请求，指定单图不回退目录首图。</summary>
    /// <param name="path">原书籍或单图定位，不是临时文件。</param><param name="archives">唯一来源工厂。</param>
    /// <param name="folders">原指定封面配置。</param><param name="token">可见需求取消。</param>
    public static async Task<ArchivePageCover> GetSelectedPageAsync(string path, IArchiveFactory archives, FolderConfigCollection folders, CancellationToken token)
    {
        var cover = new ArchivePageCover();
        try
        {
            try
            {
                if (folders.GetThumbnailTarget(path) is { } target)
                {
                    var selected = await archives.OpenAsync(target, token); cover.Own(selected);
                    cover.Entry = (await selected.GetEntriesAsync(token)).FirstOrDefault(e => e.IsImage() && e.EntryName == selected.RequestedEntryName);
                    if (cover.Entry is not null) return cover;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("指定封面：" + ex.Message); }
            var source = await archives.OpenAsync(path, token); cover.Own(source);
            var entries = await source.GetEntriesAsync(token);
            if (source.RequestedEntryName is { } requested)
            {
                cover.Entry = entries.FirstOrDefault(e => e.EntryName == requested && e.IsImage());
                return cover;
            }
            cover.Entry = await SelectEntriesAsync(entries, archives, Config.Current.Book.BookThumbnailDepth, CreateRegex(), cover, token);
            return cover;
        }
        catch { await cover.DisposeAsync(); throw; }
    }
    /// <summary>无效/超时正则回退自然首图，保持原有限匹配预算。</summary>
    private static Regex? CreateRegex()
    {
        try { return string.IsNullOrEmpty(Config.Current.Book.BookThumbnailRegex) ? null : new(Config.Current.Book.BookThumbnailRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
        catch (ArgumentException) { return null; }
    }
    /// <summary>按原首图优先、有限深度/子项数查找封面；无封面返回原空书籍页状态。</summary>
    /// <param name="page">当前实际目录或归档页面。</param><param name="token">无显示消费者或关闭取消。</param>
    public static async Task<ArchivePageCover> GetSelectedPageAsync(Page page, CancellationToken token)
    {
        var cover = new ArchivePageCover();
        try
        {
            if (page.Content.Archives is not { } archives) throw new NotSupportedException("书籍封面来源未装配。");
            // 原显式目标优先；目标失效回退默认封面，取消仍必须向上传递。
            try
            {
                if (page.Content.FolderConfigs?.GetThumbnailTarget(page.EntryFullName) is { } target)
                {
                    var source = await archives.OpenAsync(target, token); cover.Own(source);
                    var entries = await source.GetEntriesAsync(token);
                    cover.Entry = entries.FirstOrDefault(e => e.IsImage() && e.EntryName == source.RequestedEntryName);
                    if (cover.Entry is not null) return cover;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("指定封面：" + ex.Message); }
            var match = CreateRegex();
            cover.Entry = await SelectAsync(page.ArchiveEntry, archives, Config.Current.Book.BookThumbnailDepth, match, cover, token);
            return cover;
        }
        catch { await cover.DisposeAsync(); throw; }
    }
    /// <summary>保留原先找当前来源首图、再限深度寻找子书的顺序；损坏子书不阻止其他候选。</summary>
    private static async Task<ArchiveEntry?> SelectAsync(ArchiveEntry entry, IArchiveFactory archives, int depth, Regex? match, ArchivePageCover cover, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<ArchiveEntry> entries;
            if (entry.IsDirectory && !entry.Archive.IsDirectory)
            {
                var prefix = entry.EntryName.TrimEnd('/') + "/";
                entries = (await entry.Archive.GetEntriesAsync(token)).Where(e => e.EntryName.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            }
            else
            {
                var archive = await archives.OpenAsync(entry.SystemPath, token); cover.Own(archive);
                entries = await archive.GetEntriesAsync(token);
            }
            return await SelectEntriesAsync(entries, archives, depth, match, cover, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("书籍封面：" + ex.Message); return null; }
    }
    /// <summary>共用原首图/指定名/深度顺序，路径入口和正文书籍页不维护两套选择逻辑。</summary>
    private static async Task<ArchiveEntry?> SelectEntriesAsync(IReadOnlyList<ArchiveEntry> entries, IArchiveFactory archives, int depth, Regex? match, ArchivePageCover cover, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            var sorted = entries.OrderBy(e => e.EntryName, NaturalSort.Comparer).ToArray();
            // 原包内目录的整个前缀范围都参与首图选择；深度限制只作用于另行打开子书。
            if (match is not null)
                try { if (sorted.FirstOrDefault(e => e.IsImage() && match.IsMatch(System.IO.Path.GetFileName(e.EntryName))) is { } specified) return specified; }
                catch (RegexMatchTimeoutException) { /* 过长匹配回退自然首图，保持界面可操作。 */ }
            if (sorted.FirstOrDefault(e => e.IsImage()) is { } image) return image;
            if (depth > 1)
                foreach (var child in sorted.Where(e => e.IsBook() && !e.IsShortcut).Take(depth))
                    if (await SelectAsync(child, archives, depth - 1, match, cover, token) is { } selected) return selected;
            return null;
    }
}

/// <summary>请求级代表图片及其来源所有权；解码完成或取消后释放，不进入书籍持久状态。</summary>
public sealed class ArchivePageCover : IAsyncDisposable
{
    private readonly List<Archive> _sources = [];
    public ArchiveEntry? Entry { get; internal set; }
    /// <summary>已有Page来源仍由Book拥有；这个包装只用于共用读取链。</summary>
    internal static ArchivePageCover Borrow(ArchiveEntry entry) => new() { Entry = entry };
    internal void Own(Archive archive) => _sources.Add(archive);
    /// <summary>先关最内层，归档实现等待仍在执行的原生读取。</summary>
    public async ValueTask DisposeAsync() { foreach (var source in _sources.AsEnumerable().Reverse()) await source.DisposeAsync(); _sources.Clear(); }
}
/// <summary>目录或归档没有代表图片，表现端绘制正常空封面，不把目录交给图片解码。</summary>
public sealed class EmptyArchivePageException() : Exception("这个书籍没有可用的封面图片。");
