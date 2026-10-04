namespace NeeView;
public sealed partial class BookshelfFolderList
{
    public string SearchKeyword { get; private set; } = "";
    public Action<Action> Post { get; set; } = action => action();
    private IDisposable? _searchWatch;
    private string? _watchPlace;
    private bool _watchRecursive, _presented;
    private bool _searchWatchSuspended;
    private long _searchWatchId;
    private CancellationTokenSource? _watchRefresh;
    public Task SearchWatching { get; private set; } = Task.CompletedTask;
    public bool IsPresented { get => _presented; set { if (_presented == value) return; if (!value && _searchWatch is not null) _searchWatchSuspended = true; _presented = value; RefreshSearchWatch(); } }
    /// <summary>原普通书架搜索，成功才提交表达式及列表；不改变正文书籍。</summary>
    /// <param name="keyword">原搜索表达式。</param><param name="expectedPlace">输入时的书架位置，拒绝晚到动作。</param>
    /// <param name="token">新输入或关闭取消。</param><returns>候选结果是否提交到当前位置。</returns>
    public Task<bool> SearchAsync(string keyword, string? expectedPlace, CancellationToken token = default)
    {
        keyword = keyword.Trim(); SearchBookshelfCollection.Analyze(keyword);
        if (_disposed || Place != expectedPlace || Place is null) return Task.FromResult(false);
        if (IsQuickAccessPlace || IsBookmarkPlace) throw new NotSupportedException("此入口用于普通目录/归档书架；书签搜索请使用书签面板。");
        if (keyword == SearchKeyword && !IsLoading) return Task.FromResult(true);
        return SetPlaceAsync(Place, SelectedItem?.Path, token, true, keyword);
    }
    /// <summary>单一活动搜索根监视，隐藏/换位/关闭释放；重新显示补齐变化。</summary>
    private void RefreshSearchWatch()
    {
        if (_disposed || !IsPresented || SearchKeyword.Length == 0 || IsQuickAccessPlace || IsBookmarkPlace || Place is null) { StopSearchWatch(); return; }
        bool recursive = Config.Current.Bookshelf.IsSearchIncludeSubdirectories;
        if (_searchWatch is not null && _watchPlace == Place && _watchRecursive == recursive)
        {
            if (_searchWatchSuspended) { _searchWatchSuspended = false; QueueSearchRefresh(_searchWatchId); }
            return;
        }
        StopSearchWatch(); var id = ++_searchWatchId; _watchPlace = Place; _watchRecursive = recursive;
        try { _searchWatch = archives.WatchBookSearch(Place, recursive, () => Post(() => QueueSearchRefresh(id))); if (_searchWatch is not null && _searchWatchSuspended) { _searchWatchSuspended = false; QueueSearchRefresh(id); } }
        catch (Exception ex) { Error = "搜索监视不可用，可手动刷新：" + ex.Message; }
    }
    private void QueueSearchRefresh(long id)
    {
        if (_disposed || !IsPresented || id != _searchWatchId || _searchWatch is null) return;
        // 旧目录回报不能启动新请求取代用户的在途导航；待其成功/失败后按最终位置补刷新。
        if (IsLoading) { _searchWatchSuspended = true; return; }
        _watchRefresh?.Cancel(); var request = new CancellationTokenSource(); _watchRefresh = request;
        SearchWatching = RunAsync();
        async Task RunAsync()
        {
            try
            {
                await Task.Delay(250, request.Token);
                if (!_disposed && IsPresented && id == _searchWatchId)
                {
                    if (IsLoading) _searchWatchSuspended = true;
                    else await RefreshAsync(request.Token);
                }
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally { if (ReferenceEquals(_watchRefresh, request)) _watchRefresh = null; request.Dispose(); }
        }
    }
    private void StopSearchWatch() { ++_searchWatchId; _searchWatch?.Dispose(); _searchWatch = null; _watchPlace = null; _watchRefresh?.Cancel(); }
}
