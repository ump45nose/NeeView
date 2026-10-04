// Copyright (c) NeeLaboratory. 原 FolderTreeModel 的选择、Decide、沿父链同步子集；Windows 驱动器/监视后续替换。
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
namespace NeeView;

/// <summary>普通目录树独立于书架列表；展开不改变书籍或列表，确认才浏览对应目录。</summary>
public sealed class FolderTreeModel : ObservableObject, IDisposable
{
    private readonly BookshelfFolderList _bookshelf;
    private CancellationTokenSource? _sync;
    private long _revision;
    private bool _disposed;
    private DirectoryNode? _selected;
    private string? _error;
    private bool _presented = true;
    public DirectoryNode Root { get; }
    public IReadOnlyList<DirectoryNode> Roots { get; }
    public bool HasKeyboardFocus { get; set; }
    /// <summary>宿主仅回报实际显隐；隐藏取消请求，恢复时按原自动同步开关补齐位置。</summary>
    public bool IsPresented
    {
        get => _presented;
        set
        {
            if (_presented == value) return; _presented = value;
            if (!value) { HasKeyboardFocus = false; CancelPending(); }
            else PlaceChanged(this, EventArgs.Empty);
        }
    }
    public Task Synchronizing { get; private set; } = Task.CompletedTask;
    public DirectoryNode? SelectedItem
    {
        get => _selected;
        set
        {
            if (value is not null && !value.ContainsRoot(Root)) return;
            if (ReferenceEquals(value, _selected)) return;
            if (_selected is not null) { _selected.PropertyChanged -= SelectedNodeChanged; _selected.IsSelected = false; }
            SetProperty(ref _selected, value);
            if (value is not null) { value.IsSelected = true; value.PropertyChanged += SelectedNodeChanged; }
        }
    }
    /// <summary>刷新移除当前节点时退回仍有效的父节点，不保留已释放的选择引用。</summary>
    private void SelectedNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DirectoryNode.IsDisposed) || _selected is not { IsDisposed: true } node) return;
        var parent = node.Parent as DirectoryNode;
        while (parent?.IsDisposed == true) parent = parent.Parent as DirectoryNode;
        SelectedItem = _disposed ? null : parent;
    }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>构造原普通树的Mac根；不枚举磁盘，不创建Windows驱动器模拟层。</summary>
    /// <param name="archives">普通目录的既有后台来源契约。</param>
    /// <param name="bookshelf">确认目录时更新的唯一书架，正文独立。</param>
    /// <param name="root">实际文件系统根；默认Mac的/，测试可指定独立根。</param>
    public FolderTreeModel(IArchiveFactory archives, BookshelfFolderList bookshelf, string root = "/")
    { _bookshelf = bookshelf; Root = new(System.IO.Path.GetFullPath(root), null, archives); Roots = [Root]; bookshelf.Changed += PlaceChanged; }
    /// <summary>原Decide只请求书架位置，不打开目录为新书；选择来自本树才能执行。</summary>
    /// <returns>书架目录浏览是否提交成功。</returns>
    public async Task<bool> DecideAsync()
    { if (_disposed || SelectedItem is not { IsDisposed: false } node) return false; return await _bookshelf.SetPlaceAsync(node.Path); }
    /// <summary>沿目标父链展开，其他分支保持延迟；自动同步在树有焦点时不抢选择。</summary>
    /// <param name="path">普通书架真实目录路径；归档或书签地址不参与此树。</param>
    /// <param name="force">显式同步可更新选择，自动同步尊重树焦点。</param>
    /// <param name="token">调用方取消。</param>
    /// <returns>目标节点；缺失、不可达或过期时为空，现有选择保持。</returns>
    public async Task<DirectoryNode?> SyncDirectoryAsync(string? path, bool force = false, CancellationToken token = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path) || path.StartsWith("bookmark:", StringComparison.Ordinal)) return null;
        _sync?.Cancel(); var revision = ++_revision; var pending = CancellationTokenSource.CreateLinkedTokenSource(token); _sync = pending;
        try
        {
            path = System.IO.Path.GetFullPath(path);
            var relative = System.IO.Path.GetRelativePath(Root.Path, path);
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)) return null;
            var node = Root;
            foreach (var name in relative == "." ? [] : relative.Split(System.IO.Path.DirectorySeparatorChar))
            {
                if (!await node.CreateChildrenAsync(token: pending.Token))
                { if (!_disposed && revision == _revision && !pending.IsCancellationRequested) Error = node.Error; return null; }
                pending.Token.ThrowIfCancellationRequested(); if (_disposed || revision != _revision) return null;
                node.IsExpanded = true;
                var child = node.ChildrenRaw?.OfType<DirectoryNode>().FirstOrDefault(e => e.Name == name);
                if (child is null) { Error = "目标目录不在当前已枚举树中，可刷新后重试。"; return null; }
                node = child;
            }
            if (_disposed || revision != _revision) return null;
            Error = null; if (force || !HasKeyboardFocus) SelectedItem = node; return node;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return null; }
        catch (Exception ex) { if (!_disposed && revision == _revision) Error = "目录暂不可访问：" + ex.Message; return null; }
        finally { if (ReferenceEquals(_sync, pending)) _sync = null; pending.Dispose(); }
    }
    /// <summary>原自动同步开关只在树启用时响应成功的书架位置，不随翻页扫描。</summary>
    private void PlaceChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsPresented || _bookshelf.IsLoading || !Config.Current.Bookshelf.IsFolderTreeVisible || !Config.Current.Bookshelf.IsSyncFolderTreeAuto) return;
        if (SelectedItem?.Path == _bookshelf.Place) return;
        Synchronizing = SyncDirectoryAsync(_bookshelf.Place);
    }
    /// <summary>显式刷新当前节点保留已展开后代及选择；失败保留旧可用子项。</summary>
    /// <param name="token">调用方关闭或取消。</param>
    /// <returns>刷新成功提交；失败/过期为false。</returns>
    public Task<bool> RefreshDirectoryAsync(CancellationToken token = default) => (SelectedItem ?? Root).CreateChildrenAsync(true, token);
    /// <summary>隐藏时取消正在进行的路径同步和已加载分支需求，保留元数据供下次显示。</summary>
    public void CancelPending()
    {
        ++_revision; _sync?.Cancel();
        Cancel(Root);
        static void Cancel(DirectoryNode node) { node.CancelLoading(); foreach (var child in node.ChildrenRaw?.OfType<DirectoryNode>() ?? []) Cancel(child); }
    }
    /// <summary>BookOperation关闭时解除书架订阅并拒绝所有晚到结果。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; SelectedItem = null; CancelPending(); _bookshelf.Changed -= PlaceChanged; Root.Dispose(); }
}
