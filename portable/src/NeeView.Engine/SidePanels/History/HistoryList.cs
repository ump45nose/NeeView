// Copyright (c) NeeLaboratory. 移植自 SidePanels/History/HistoryList.cs，沿用仓库 MIT。
namespace NeeView;

/// <summary>原历史列表的过滤与前后浏览规则；不依赖 WPF CollectionView 或界面选择。</summary>
public sealed class HistoryList(SaveData saveData) : IDisposable
{
    private long _revision;
    private bool _disposed;
    private CancellationTokenSource? _pending;
    private HashSet<string>? _matches;
    public string SearchKeyword { get; private set; } = "";
    public string? Address { get; set; }
    public string? FilterPath => Config.Current.History.IsCurrentFolder && Address is not null ? LoosePath.GetDirectoryName(Address) : null;

    /// <summary>按原访问倒序过滤直接父目录与已提交搜索；UI和导航均不执行正则或I/O。</summary>
    /// <returns>只读展示序列；不改动权威历史、排序或源文件。</returns>
    public IReadOnlyList<HistoryEntry> GetViewItems() => saveData.HistoryEntries.Where(item =>
        (string.IsNullOrEmpty(FilterPath) || FilterPath == LoosePath.GetDirectoryName(item.Path)) &&
        (_matches is null || _matches.Contains(item.Path))).ToArray();

    /// <summary>捕获原历史与书签成员后后台筛选；失败、取消及环境晚到保留旧表达式/结果。</summary>
    /// <param name="keyword">已Trim的原查询。</param><param name="metadata">只有大小谓词使用的后台来源接口。</param>
    /// <param name="token">调用方或窗口关闭取消。</param><returns>本代结果是否提交。</returns>
    public async Task<bool> SearchAsync(string keyword, Func<string, CancellationToken, Task<FolderItem?>>? metadata = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var keys = SearchHistoryCollection.Analyze(keyword);
        CancelSearch(); var revision = _revision; var place = FilterPath;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token); _pending = pending;
        var items = saveData.HistoryEntries.Where(item => string.IsNullOrEmpty(place) || place == LoosePath.GetDirectoryName(item.Path))
            .Select(entry => new HistorySearchItem(entry, saveData.IsBookmark(entry.Path))).ToArray();
        try
        {
            var matches = await Task.Run(() => SearchHistoryCollection.SearchAsync(keyword, items, metadata, pending.Token), pending.Token);
            if (_disposed || revision != _revision || pending.IsCancellationRequested || place != FilterPath) return false;
            var live = saveData.HistoryEntries.Where(item => string.IsNullOrEmpty(place) || place == LoosePath.GetDirectoryName(item.Path)).ToArray();
            // 保存页位置不影响匹配；成员、访问日期或书签谓词改变时拒绝旧快照。
            if (!live.Select(item => item.Path).ToHashSet(StringComparer.Ordinal).SetEquals(items.Select(item => item.Entry.Path))) return false;
            var liveByPath = live.GroupBy(entry => entry.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (keys.Any(key => key.Property.Name == "date") && items.Any(item => liveByPath[item.Entry.Path].LastAccessTime != item.Entry.LastAccessTime)) return false;
            if (keys.Any(key => key.Property.Name == "bookmark") && items.Any(item => saveData.IsBookmark(item.Entry.Path) != item.IsBookmark)) return false;
            SearchKeyword = keyword; _matches = keyword.Length == 0 ? null : matches; return true;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return false; }
        finally { if (ReferenceEquals(_pending, pending)) _pending = null; }
    }

    /// <summary>撤销排队需求并使不可中断的晚到结果失效；已提交搜索保持。</summary>
    public void CancelSearch() { Interlocked.Increment(ref _revision); _pending?.Cancel(); _pending = null; }
    /// <summary>由阅读宿主释放，拒绝关闭后新的或晚到的查询。</summary>
    public void Dispose() { _disposed = true; CancelSearch(); }

    /// <summary>保留原 PrevHistory/NextHistory：后退找较旧项，前进找较新项。</summary>
    /// <param name="direction">-1 为较旧，1 为较新；当前项不在筛选结果中时只有后退可选首项。</param>
    /// <returns>实际目标；到边界时为 null，不循环。</returns>
    public HistoryEntry? GetTarget(int direction)
    {
        var items = GetViewItems();
        var index = items.ToList().FindIndex(item => item.Path == Address);
        if (index < 0) return direction < 0 ? items.FirstOrDefault() : null;
        int target = index + (direction < 0 ? 1 : -1);
        return target >= 0 && target < items.Count ? items[target] : null;
    }

    /// <summary>原日期分组：今天、昨天或当地完整日期，时间不因分组而重写。</summary>
    /// <param name="time">历史原访问时间。</param>
    /// <param name="today">测试或表现端传入当前本地日期。</param>
    public static string GetGroupName(DateTime time, DateTime today)
    {
        var date = (time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time).Date;
        return date == today.Date ? "今天" : date == today.Date.AddDays(-1) ? "昨天" : date.ToString("D", System.Globalization.CultureInfo.CurrentCulture);
    }
}
