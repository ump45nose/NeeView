// Copyright (c) NeeLaboratory. 原 DestinationFolderPanelViewModel 两区/按目录刷新控制，基线 c5c398d89。
namespace NeeView;

/// <summary>原分类面板的数据控制；界面布局、列表选中和对话框留在Mac表现层。</summary>
public sealed class DestinationFolderPanel : IDisposable
{
    private readonly BookOperation _operation;
    private readonly IArchiveFactory _archives;
    private CancellationTokenSource? _refresh;
    private string? _directory;
    private bool _disposed;
    private bool _auto = Config.Current.Panels.IsDestinationFolderAutoRefreshEnabled;
    public DestinationFolderPanel(BookOperation operation, IArchiveFactory archives)
    { _operation = operation; _archives = archives; operation.Changed += Operation_Changed; RefreshManaged(); Operation_Changed(this, EventArgs.Empty); }
    public IReadOnlyList<DestinationFolderPanelItem> ManagedFolders { get; private set; } = [];
    public IReadOnlyList<DestinationFolderPanelItem> CurrentFolderChildren { get; private set; } = [];
    public string? CurrentDirectory => _directory;
    public string? Error { get; private set; }
    public bool IsRefreshing { get; private set; }
    public event EventHandler? Changed;
    public void RefreshManaged()
    { ManagedFolders = Config.Current.System.DestinationFolderCollection.Select((folder, index) => new DestinationFolderPanelItem(folder, index + 1)).ToArray(); Changed?.Invoke(this, EventArgs.Empty); }
    private void Operation_Changed(object? sender, EventArgs e)
    {
        var directory = _operation.CurrentPictureDirectory; bool auto = Config.Current.Panels.IsDestinationFolderAutoRefreshEnabled;
        bool changed = directory != _directory, enabled = auto && !_auto; _auto = auto;
        if (changed) { _directory = directory; _refresh?.Cancel(); CurrentFolderChildren = []; Error = null; }
        if (!auto) { _refresh?.Cancel(); IsRefreshing = false; }
        if ((changed || enabled) && auto) _ = RefreshChildrenAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>只读取直接子目录；切书/关闭/关闭自动刷新拒绝晚到枚举。</summary>
    public async Task RefreshChildrenAsync(CancellationToken token = default)
    {
        if (_disposed) return;
        _refresh?.Cancel(); var request = CancellationTokenSource.CreateLinkedTokenSource(token); _refresh = request;
        string? directory = _directory; IsRefreshing = true; Error = null; Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var items = directory is null ? [] : await _archives.ListFoldersAsync(directory, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(_refresh, request) || directory != _directory) return;
            CurrentFolderChildren = items.Where(item => !item.IsSymbolicLink).Select(item => new DestinationFolderPanelItem(new(item.Name, item.Path), null)).ToArray();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && !request.IsCancellationRequested && ReferenceEquals(_refresh, request)) Error = ex.Message; }
        finally
        {
            if (ReferenceEquals(_refresh, request)) { _refresh = null; IsRefreshing = false; if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
            request.Dispose();
        }
    }
    public void Dispose() { _disposed = true; _operation.Changed -= Operation_Changed; _refresh?.Cancel(); }
}
public sealed record DestinationFolderPanelItem(DestinationFolder Folder, int? Number)
{
    public string Name => Folder.Name;
    public string Path => Folder.Path;
    public string Label => Number is null ? Name : $"{Number}  {Name}";
}
