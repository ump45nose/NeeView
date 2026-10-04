// Copyright (c) NeeLaboratory. 原SearchBoxModel的Trim/Analyze、500ms输入、确认历史与取消表现适配。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;
/// <summary>页面/书架共用搜索输入表现；过滤、来源和JSON全部由原Engine入口负责。</summary>
public sealed class NavigationSearchViewModel(Func<object?> scope, Action<string> analyze,
    Func<string, object?, CancellationToken, Task<bool>> search, HistoryStringCollection history,
    Func<string, bool, CancellationToken, Task> editHistory) : ObservableObject, IDisposable
{
    private string _keyword = "";
    private string? _error;
    private object? _scope = scope();
    private CancellationTokenSource? _pending;
    private bool _closing, _disposed;
    private readonly HashSet<Task> _tasks = [];
    public HistoryStringCollection History => history;
    public bool IsSearching => _pending is not null;
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public event EventHandler? Refreshed;
    /// <summary>坏草稿只显示错误；有效输入沿原全局增量开关执行。</summary>
    public string Keyword
    {
        get => _keyword;
        set { if (_disposed || _closing || !SetProperty(ref _keyword, value)) return; _pending?.Cancel(); if (Validate() && Config.Current.System.IsIncrementalSearchEnabled) _ = RunAsync(false, true); }
    }
    private bool Validate()
    { try { analyze(Keyword.Trim()); Error = null; return true; } catch (Exception ex) { Error = ex.Message; Refreshed?.Invoke(this, EventArgs.Empty); return false; } }
    /// <summary>明确确认才写原搜索历史；查询取消不撤销已确认保存。</summary>
    /// <param name="recordHistory">确认输入为true，环境重筛/清空为false。</param><returns>过滤及所需保存均成功时为true。</returns>
    public Task<bool> SearchAsync(bool recordHistory = true) => !_disposed && !_closing && Validate() ? RunAsync(recordHistory, false) : Task.FromResult(false);
    private async Task<bool> RunAsync(bool record, bool debounce)
    {
        _pending?.Cancel(); var request = new CancellationTokenSource(); _pending = request;
        var keyword = Keyword.Trim(); var expected = scope(); Notify(); var task = ExecuteAsync(); _tasks.Add(task);
        try { return await task; }
        finally { _tasks.Remove(task); if (ReferenceEquals(_pending, request)) { _pending = null; Notify(); } request.Dispose(); }
        async Task<bool> ExecuteAsync()
        {
            try
            {
                string? saveError = null;
                if (record && keyword.Length > 0) { try { await editHistory(keyword, false, CancellationToken.None); } catch (Exception ex) { saveError = "搜索历史保存失败：" + ex.Message; } }
                if (debounce) await Task.Delay(500, request.Token);
                if (!Equals(scope(), expected) || request.IsCancellationRequested) return false;
                var success = await search(keyword, expected, request.Token);
                if (!_disposed && !_closing && !request.IsCancellationRequested) Error = saveError;
                return success && saveError is null;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
            catch (Exception ex) { if (!_disposed && !_closing && !request.IsCancellationRequested) Error = ex.Message; return false; }
        }
    }
    /// <summary>换书/目录取消旧输入及晚到结果；翻页不重搜。</summary>
    public void RefreshScope()
    { var current = scope(); if (Equals(current, _scope)) return; _scope = current; _pending?.Cancel(); _keyword = ""; Error = null; Notify(); }
    /// <summary>删除指定原历史表达式；失败恢复集合并显示独立错误，关闭等待已授权编辑。</summary>
    /// <param name="keyword">待删除表达式。</param><returns>可等待的保存任务。</returns>
    public async Task RemoveHistoryAsync(string keyword)
    {
        if (_disposed || _closing) return; var task = editHistory(keyword, true, CancellationToken.None); _tasks.Add(task);
        try { await task; } catch (Exception ex) { Error = ex.Message; } finally { _tasks.Remove(task); Notify(); }
    }
    private void Notify() { if (!_disposed) { OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty); } }
    /// <summary>禁止新输入，取消显示需求并等待已经确认的保存及查询。</summary>
    /// <returns>所有已登记任务结束后的关闭任务。</returns>
    public async Task PrepareCloseAsync() { _closing = true; _pending?.Cancel(); await Task.WhenAll(_tasks.ToArray()); }
    /// <summary>窗口退出保存失败后恢复同一输入实例，允许用户重试。</summary>
    public void CancelClose() => _closing = false;
    /// <summary>退订宿主后释放输入资格并取消剩余需求，晚到回报不通知界面。</summary>
    public void Dispose() { _disposed = true; _pending?.Cancel(); }
}
