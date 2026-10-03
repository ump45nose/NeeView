using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>书签列表独立表现模型；结构和主题可变，导航次序由原业务适配计算。</summary>
public sealed class BookmarkListViewModel : ObservableObject, IDisposable
{
    private readonly SaveData _state;
    private bool _disposed;
    private bool _closing;
    private string _keyword = "";
    private string? _searchError;
    private CancellationTokenSource? _pendingSearch;
    private readonly HashSet<Task> _searches = [];
    private HashSet<string> _historyPaths = [];
    public Func<string, CancellationToken, Task<FolderItem?>>? ReadMetadataAsync { get; set; }
    public bool SearchVisible => Config.Current.Bookmark.IsVisibleSearchBox;
    public bool IsSearching => List.IsSearching || _pendingSearch is not null;
    public string? SearchError { get => _searchError; private set => SetProperty(ref _searchError, value); }
    public HistoryStringCollection SearchHistory => _state.BookmarkSearchHistory;
    /// <summary>原 SearchBoxModel.OnKeywordChanged：Trim、校验、可选增量；错误不替换有效结果。</summary>
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (_disposed || _closing || !SetProperty(ref _keyword, value)) return;
            CancelSearch();
            try { SearchBookmarkFolderCollection.Analyze(value.Trim()); SearchError = null; }
            catch (Exception ex) { SearchError = ex.Message; return; }
            if (Config.Current.System.IsIncrementalSearchEnabled) _ = SearchAsync(debounce: true);
        }
    }
    public BookmarkFolderList List { get; }
    public IReadOnlyList<BookmarkNode> Items => List.Items;
    public string Place => List.FullPath;
    public string Count => IsSearching ? "搜索中…" : $"{Items.Count} 项";
    public bool CountVisible => Config.Current.Bookmark.IsVisibleItemsCount;
    public bool TreeVisible => Config.Current.Bookmark.IsFolderTreeVisible;
    public bool CanMoveToParent => List.CanMoveToParent;
    public string? CapabilityMessage => List.CapabilityMessage;
    public static IReadOnlyList<BookmarkOrderChoice> Orders { get; } =
    [new(FolderOrder.FileName, "名称"), new(FolderOrder.FileNameDescending, "名称（降序）"),
     new(FolderOrder.Path, "路径"), new(FolderOrder.PathDescending, "路径（降序）"),
     new(FolderOrder.FileType, "类型"), new(FolderOrder.FileTypeDescending, "类型（降序）"),
     new(FolderOrder.TimeStamp, "时间"), new(FolderOrder.TimeStampDescending, "时间（降序）"),
     new(FolderOrder.Size, "大小"), new(FolderOrder.SizeDescending, "大小（降序）"),
     new(FolderOrder.EntryTime, "注册顺序"), new(FolderOrder.EntryTimeDescending, "注册顺序（逆序）"), new(FolderOrder.Random, "随机")];
    public BookmarkOrderChoice SelectedOrder => Orders.First(choice => choice.Mode == List.FolderOrder);
    public event EventHandler? Refreshed;
    public event EventHandler<string>? Failed;

    /// <summary>接入唯一已加载集合，只订阅书签事务回报，不因翻页保存重复重排。</summary>
    public BookmarkListViewModel(SaveData state)
    {
        _state = state; List = new(state.Bookmarks, state.FolderConfigs); state.BookmarksChanged += Bookmarks_Changed;
        state.Changed += State_Changed;
        _historyPaths = state.HistoryEntries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        if (Config.Current.StartUp is { IsOpenLastBookmarkFolder: true, LastBookmarkFolder: { } last } && List.FindFolder(last.Path) is { } folder)
        {
            last.Register(state.FolderConfigs);
            List.SetPlace(folder, folder.Children?.FirstOrDefault(node => List.GetTargetPath(node) == last.Select));
        }
    }
    /// <summary>后台提交先回到 UI 线程；窗口已释放时忽略晚到回报。</summary>
    private void Bookmarks_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => Refresh(resetInput: false));
    /// <summary>/history 依赖真实访问记录；只有该属性的已提交查询才随进度保存重新筛选。</summary>
    private void State_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || _closing || !List.HasHistoryPredicate) return;
        var paths = _state.HistoryEntries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        // 进度/时间更新不改变 /history 的布尔值，避免一秒防抖保存反复扫描书签。
        if (_historyPaths.SetEquals(paths)) return;
        _historyPaths = paths; _ = SearchCoreAsync(List.SearchKeyword, false, false, preserveDraft: true);
    });
    /// <summary>同步原节点身份和列表表现，不发布阅读刷新。</summary>
    public void Refresh(bool resetInput = false)
    {
        if (_disposed) return;
        var hadPendingInput = _pendingSearch is not null; var place = List.Place;
        CancelSearch(); List.ReadMetadataAsync = ReadMetadataAsync; List.Refresh(); resetInput |= !ReferenceEquals(place, List.Place);
        // 导航成功后的环境重置只改输入，不再次触发增量搜索或登记空历史。
        if (resetInput) { _keyword = List.SearchKeyword; SearchError = null; }
        Notify();
        if (_closing) return;
        var query = List.SearchKeyword;
        // 表现/集合变化不丢弃尚在等待的有效输入；无效草稿只重筛上一次有效表达式。
        if (!resetInput && SearchError is null && (hadPendingInput || Config.Current.System.IsIncrementalSearchEnabled)) query = Keyword.Trim();
        if (query.Length > 0 || List.SearchKeyword.Length > 0) _ = SearchCoreAsync(query, false, false, preserveDraft: !resetInput);
        else TrackMetadata();
    }

    /// <summary>每目录排序进入原Foldres事务；保存失败恢复参数/选择，不改变全局默认。</summary>
    public async Task ChangeOrderAsync(FolderOrder mode)
    {
        try { await _state.EditFolderParametersAsync(() => List.ChangeOrder(mode)); }
        finally { List.Refresh(); Notify(); }
        await LoadMetadataAsync();
    }
    /// <summary>后台补齐只重排列表；真实来源失败显示独立提示，不删除书签或改保存的排序。</summary>
    private void TrackMetadata()
    {
        var task = LoadMetadataAsync(); _searches.Add(task);
        _ = FinishAsync();
        async Task FinishAsync() { try { await task; } finally { _searches.Remove(task); } }
    }
    private async Task LoadMetadataAsync()
    {
        try { if (await List.LoadMetadataAsync() && !_disposed && !_closing) Notify(); }
        catch (Exception ex) { if (!_disposed && !_closing) { SearchError = "来源元数据暂不可访问：" + ex.Message; Notify(); } }
    }

    /// <summary>确认/失焦先登记语法有效的原历史再等待查询；逐次输入不登记。</summary>
    /// <param name="recordHistory">Enter 或失焦的确定查询。</param><param name="debounce">合并短时间连续输入，不改变匹配规则。</param>
    /// <returns>本次有效结果是否提交；错误保留旧结果并显示。</returns>
    public async Task<bool> SearchAsync(bool recordHistory = false, bool debounce = false)
    {
        if (_disposed || _closing) return false;
        // 读取/落盘失败后允许同一有效表达式重试；只有语法仍然无效才拒绝。
        try { SearchBookmarkFolderCollection.Analyze(Keyword.Trim()); SearchError = null; }
        catch (Exception ex) { SearchError = ex.Message; return false; }
        return await SearchCoreAsync(Keyword.Trim(), recordHistory, debounce, preserveDraft: false);
    }

    /// <summary>同一查询入口可重筛已提交表达式；书签/历史变化不确认尚未提交的草稿。</summary>
    private async Task<bool> SearchCoreAsync(string keyword, bool recordHistory, bool debounce, bool preserveDraft)
    {
        if (_disposed || _closing) return false;
        CancelSearch(); var pending = new CancellationTokenSource(); _pendingSearch = pending;
        var priorError = SearchError;
        Notify(); var task = RunSearchAsync(); _searches.Add(task);
        try { return await task; }
        finally
        {
            _searches.Remove(task);
            if (ReferenceEquals(_pendingSearch, pending)) { _pendingSearch = null; Notify(); }
            pending.Dispose();
        }

        // 查询任务跟踪所有请求；取消后不可中断 I/O 仍由后端观察，晚到不能提交。
        async Task<bool> RunSearchAsync()
        {
            try
            {
                string? historyError = null;
                // 原 SearchBoxModel 确认有效语法即登记历史，不等后台匹配成功；之后删除不会被晚到查询重新追加。
                if (recordHistory && keyword.Length > 0)
                {
                    try { await _state.EditBookmarkSearchHistoryAsync(keyword); }
                    catch (Exception ex)
                    {
                        // 已确认的保存失败不能被随后输入/关闭的取消标志隐藏；原集合已回滚。
                        historyError = "搜索历史保存失败：" + ex.Message;
                        if (!_disposed) { SearchError = historyError; Failed?.Invoke(this, historyError); }
                    }
                }
                if (debounce) await Task.Delay(150, pending.Token);
                var submitted = await List.SearchAsync(keyword, _state.HistoryEntries.Select(entry => entry.Path), ReadMetadataAsync, pending.Token);
                if (!submitted || _disposed || _closing || pending.IsCancellationRequested) return false;
                SearchError = historyError ?? (preserveDraft ? priorError : null); Notify();
                await LoadMetadataAsync();
                return historyError is null;
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { return false; }
            catch (Exception ex) { if (!_disposed && !_closing && !pending.IsCancellationRequested) SearchError = ex.Message; return false; }
        }
    }

    /// <summary>删除原搜索历史的指定表达式；失败由原事务恢复，不改变搜索文本或正文。</summary>
    public Task RemoveHistoryAsync(string keyword) => _state.EditBookmarkSearchHistoryAsync(keyword, remove: true);

    /// <summary>已开始搜索输入可取消；列表已提交结果保持，窗口关闭后不接受新任务。</summary>
    private void CancelSearch() { _pendingSearch?.Cancel(); _pendingSearch = null; List.CancelSearch(); }
    /// <summary>统一发布界面快照，搜索结果只更新列表，不请求正文资源。</summary>
    private void Notify() { if (!_disposed) { OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty); } }
    /// <summary>关闭取消并等待查询/确认历史，避免存储提前释放。</summary>
    public async Task PrepareCloseAsync()
    {
        _closing = true; CancelSearch(); await Task.WhenAll(_searches.ToArray());
        Config.Current.StartUp.LastBookmarkFolder = Config.Current.StartUp.IsOpenLastBookmarkFolder
            ? BookshelfFolderMemento.Create(List.ParameterPath, List.SelectedItem is { } node ? List.GetTargetPath(node) : null, _state.FolderConfigs) : null;
    }
    /// <summary>退出保存失败后重新允许输入。</summary>
    public void CancelClose() => _closing = false;
    /// <summary>关闭面板退订所有状态通知；唯一集合与 SaveData 继续由宿主维护。</summary>
    public void Dispose() { _disposed = true; CancelSearch(); List.Dispose(); _state.BookmarksChanged -= Bookmarks_Changed; _state.Changed -= State_Changed; }
}

/// <summary>原排序项的界面文案；未迁移项保留禁用占位。</summary>
public sealed record BookmarkOrderChoice(FolderOrder Mode, string Label)
{
    public bool IsEnabled => BookmarkFolderList.SupportsOrder(Mode);
}
