using System.ComponentModel;
namespace NeeView;
public sealed partial class FolderTreeModel
{
    private readonly IArchiveFactory _archives;
    private readonly HashSet<DirectoryNode> _observedDirectories = [];
    private readonly Dictionary<DirectoryNode, (IDisposable Watch, long Id)> _directoryWatches = [];
    private readonly HashSet<DirectoryNode> _dirtyDirectories = [];
    private readonly HashSet<DirectoryNode> _unobservedDirectories = [];
    private CancellationTokenSource? _watchRequest;
    private long _watchId;
    /// <summary>宿主回报线程切换，不携带控件；默认适用于没有UI的模块测试。</summary>
    public Action<Action> Post { get; set; } = action => action();
    public Task Watching { get; private set; } = Task.CompletedTask;
    public int WatchCount => _directoryWatches.Count;
    /// <summary>最多32个已加载且展开的目录，隐藏立即释放；未展开目录不递归扫描或监视。</summary>
    private void RefreshWatchers()
    {
        if (_disposed) return;
        var nodes = Roots.OfType<DirectoryNode>().SelectMany(WalkDirectories).Where(node => !node.IsDisposed).ToArray();
        foreach (var old in _observedDirectories.Except(nodes).ToArray()) { old.PropertyChanged -= DirectoryChanged; _observedDirectories.Remove(old); }
        foreach (var node in nodes) if (_observedDirectories.Add(node)) node.PropertyChanged += DirectoryChanged;
        var targets = IsPresented ? nodes.Where(node => node.ChildrenRaw is not null && node.IsExpanded).Take(32).ToHashSet() : [];
        foreach (var node in _directoryWatches.Keys.Except(targets).ToArray()) { _directoryWatches[node].Watch.Dispose(); _directoryWatches.Remove(node); if (!node.IsDisposed) _unobservedDirectories.Add(node); }
        foreach (var node in targets.Except(_directoryWatches.Keys))
        {
            try
            {
                var id = ++_watchId;
                var watch = _archives.WatchDirectory(node.Path, () => Post(() => QueueWatchRefresh(node, id)));
                if (watch is not null) { _directoryWatches.Add(node, (watch, id)); if (_unobservedDirectories.Remove(node)) QueueWatchRefresh(node, id); }
            }
            catch (Exception ex) { Error = "目录监视不可用，可手动刷新：" + ex.Message; }
        }
        if (!IsPresented) { _watchRequest?.Cancel(); _dirtyDirectories.Clear(); }
    }
    private static IEnumerable<DirectoryNode> WalkDirectories(DirectoryNode node)
    { yield return node; foreach (var child in node.ChildrenRaw?.OfType<DirectoryNode>() ?? []) foreach (var item in WalkDirectories(child)) yield return item; }
    private void DirectoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DirectoryNode.Children) or nameof(DirectoryNode.IsExpanded) or nameof(DirectoryNode.IsDisposed)) RefreshWatchers();
    }
    /// <summary>原创建/删除/重命名事件合并250ms，再经同一节点刷新链保留有效引用和选择。</summary>
    private void QueueWatchRefresh(DirectoryNode node, long id)
    {
        if (_disposed || !IsPresented || !_directoryWatches.TryGetValue(node, out var active) || active.Id != id) return;
        _dirtyDirectories.Add(node); _watchRequest?.Cancel(); var request = new CancellationTokenSource(); _watchRequest = request;
        Watching = RunAsync();
        async Task RunAsync()
        {
            try
            {
                await Task.Delay(250, request.Token); request.Token.ThrowIfCancellationRequested();
                var dirty = _dirtyDirectories.ToArray(); _dirtyDirectories.Clear();
                foreach (var item in dirty)
                    if (!_disposed && IsPresented && _directoryWatches.ContainsKey(item)) await item.CreateChildrenAsync(true, request.Token);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally { if (ReferenceEquals(_watchRequest, request)) _watchRequest = null; request.Dispose(); }
        }
    }
    private void StopWatchers()
    { _watchRequest?.Cancel(); foreach (var watch in _directoryWatches.Values) watch.Watch.Dispose(); _directoryWatches.Clear(); foreach (var node in _observedDirectories) node.PropertyChanged -= DirectoryChanged; _observedDirectories.Clear(); _dirtyDirectories.Clear(); _unobservedDirectories.Clear(); }
}
