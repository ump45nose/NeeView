// Copyright (c) NeeLaboratory. 按原 SearchBoxModel 和 HistorySearchBoxComponent 流程适配可等待保存/查询。
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>历史查询输入的独立表现；不拥有列表布局、阅读规则或系统来源。</summary>
public sealed class HistorySearchViewModel(SaveData state, HistoryList list) : ObservableObject, IDisposable
{
    private bool _disposed, _closing;
    private string _keyword = "", _activeKeyword = "";
    private string? _error;
    private CancellationTokenSource? _pending;
    private bool _pendingInput;
    private readonly HashSet<Task> _tasks = [];
    private QueryStamp[] _observed = [];
    private string? _observedPlace;
    public Func<string, CancellationToken, Task<FolderItem?>>? ReadMetadataAsync { get; set; }
    public HistoryStringCollection History => state.BookHistorySearchHistory;
    public bool IsSearching => _pending is not null;
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public event EventHandler? Refreshed;

    /// <summary>沿原Trim、Analyze和增量开关；500ms合并输入，坏草稿不覆盖有效列表。</summary>
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (_disposed || _closing || !SetProperty(ref _keyword, value)) return;
            CancelSearch();
            try { SearchHistoryCollection.Analyze(value.Trim()); Error = null; }
            catch (Exception ex) { Error = ex.Message; Notify(); return; }
            if (Config.Current.System.IsIncrementalSearchEnabled) _ = SearchCoreAsync(value.Trim(), false, true, false);
            else Notify();
        }
    }

    /// <summary>Enter/失焦先登记有效表达式；大小读取失败不妨碍保存原确认历史。</summary>
    /// <param name="recordHistory">确认搜索为true，输入/环境重筛为false。</param><returns>筛选及可选历史保存是否成功。</returns>
    public Task<bool> SearchAsync(bool recordHistory = true)
    {
        if (_disposed || _closing) return Task.FromResult(false);
        try { SearchHistoryCollection.Analyze(Keyword.Trim()); Error = null; }
        catch (Exception ex) { Error = ex.Message; Notify(); return Task.FromResult(false); }
        // 原空的确定搜索不追加历史；已经是全量时不重复刷新，避免失焦改变点击中的列表容器。
        if (Keyword.Trim().Length == 0 && list.SearchKeyword.Length == 0 && _pending is null) return Task.FromResult(true);
        return SearchCoreAsync(Keyword.Trim(), recordHistory, false, false);
    }

    /// <summary>历史/目录/书签变化仅重筛有效表达式；页位置保存不反复触发大小I/O。</summary>
    public void RefreshEnvironment()
    {
        if (_disposed || _closing) return;
        // 菜单关闭逐次搜索时取消未确认的输入，保留文字供Enter确认；已授权事务不被撤销。
        if (_pendingInput && !Config.Current.System.IsIncrementalSearchEnabled) { CancelSearch(); Notify(); }
        var query = _pending is not null ? _activeKeyword : list.SearchKeyword;
        var current = Capture(query); var place = list.FilterPath;
        if (place == _observedPlace && current.SequenceEqual(_observed)) return;
        _observed = current; _observedPlace = place;
        if (query.Length > 0) _ = SearchCoreAsync(query, false, false, true);
    }

    /// <summary>捕获真正影响本查询的字段，忽略Page和未查询的访问时间；不读取文件。</summary>
    /// <param name="query">已有效的原表达式。</param><returns>按路径稳定排列的输入指纹。</returns>
    private QueryStamp[] Capture(string query)
    {
        var keys = SearchHistoryCollection.Analyze(query);
        var date = keys.Any(key => key.Property.Name == "date"); var bookmark = keys.Any(key => key.Property.Name == "bookmark");
        return state.HistoryEntries.Select(entry => new QueryStamp(entry.Path, date ? entry.LastAccessTime : default,
            bookmark && state.IsBookmark(entry.Path))).OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>跟踪取消、关闭和已确认事务；旧结果不能替换新输入，草稿错误可独立保持。</summary>
    /// <param name="keyword">有效表达式。</param><param name="recordHistory">确认时登记历史。</param>
    /// <param name="debounce">沿原500ms输入合并。</param><param name="preserveDraft">环境变化保留草稿错误。</param>
    /// <returns>筛选和可选持久化是否成功。</returns>
    private async Task<bool> SearchCoreAsync(string keyword, bool recordHistory, bool debounce, bool preserveDraft)
    {
        if (_disposed || _closing) return false;
        var input = debounce || (preserveDraft && _pendingInput);
        CancelSearch(); var pending = new CancellationTokenSource(); _pending = pending; _activeKeyword = keyword; _pendingInput = input;
        _observed = Capture(keyword); _observedPlace = list.FilterPath; var oldError = Error;
        Notify(); var task = RunAsync(); _tasks.Add(task);
        try { return await task; }
        finally
        {
            _tasks.Remove(task);
            if (ReferenceEquals(_pending, pending)) { _pending = null; _pendingInput = false; Notify(); }
            pending.Dispose();
        }

        // 已确认历史不被后续输入撤销，关闭等待它完成；筛选的取消只影响显示需求。
        async Task<bool> RunAsync()
        {
            try
            {
                string? saveError = null;
                if (recordHistory && keyword.Length > 0)
                {
                    try { await state.EditBookHistorySearchHistoryAsync(keyword); }
                    catch (Exception ex) { saveError = "搜索历史保存失败：" + ex.Message; if (!_disposed) Error = saveError; }
                }
                if (debounce) await Task.Delay(500, pending.Token);
                var submitted = await list.SearchAsync(keyword, ReadMetadataAsync, pending.Token);
                if (!submitted || _disposed || _closing || pending.IsCancellationRequested) return false;
                Error = saveError ?? (preserveDraft ? oldError : null); return saveError is null;
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { return false; }
            catch (Exception ex) { if (!_disposed && !_closing && !pending.IsCancellationRequested) Error = ex.Message; return false; }
        }
    }

    /// <summary>删除一个原搜索表达式，观察失败；不会清空书籍访问记录或当前搜索。</summary>
    /// <param name="keyword">历史菜单指定项。</param>
    public async Task RemoveHistoryAsync(string keyword)
    {
        if (_disposed || _closing) return;
        var task = state.EditBookHistorySearchHistoryAsync(keyword, remove: true); _tasks.Add(task);
        try { await task; }
        catch (Exception ex) { Error = "搜索历史删除失败：" + ex.Message; Notify(); }
        finally { _tasks.Remove(task); }
    }
    /// <summary>取消显示需求并拒绝旧代结果；已提交表达式和历史事务保持。</summary>
    private void CancelSearch() { _pending?.Cancel(); _pending = null; _pendingInput = false; list.CancelSearch(); }
    /// <summary>只发布搜索表现和列表刷新，不触发正文解码或窗口激活。</summary>
    private void Notify() { if (!_disposed) { OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty); } }
    /// <summary>关闭前拒绝新输入，取消筛选并等待已开始的确认保存。</summary>
    public async Task PrepareCloseAsync() { _closing = true; CancelSearch(); await Task.WhenAll(_tasks.ToArray()); }
    /// <summary>正常退出失败时恢复查询入口。</summary>
    public void CancelClose() => _closing = false;
    /// <summary>取消旧面板回报；原HistoryList仍由阅读宿主释放。</summary>
    public void Dispose() { _disposed = true; CancelSearch(); }
    private sealed record QueryStamp(string Path, DateTime AccessTime, bool IsBookmark);
}
