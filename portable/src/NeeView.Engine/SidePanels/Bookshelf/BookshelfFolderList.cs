// Copyright (c) NeeLaboratory. 原 BookshelfFolderList.SyncAsync、FolderList.GetFolderItem/MoveNextFolder 子集适配。
namespace NeeView;

/// <summary>独立书架位置、列表及选择；浏览目录不改变当前书籍，所有枚举由后端完成。</summary>
public sealed class BookshelfFolderList(IArchiveFactory archives, FolderConfigCollection? folderConfigs = null, SaveData? state = null) : IDisposable
{
    private CancellationTokenSource? _request;
    private long _revision;
    private bool _disposed;
    private IReadOnlyList<FolderItem> _entries = [];
    private readonly FolderConfigCollection _folderConfigs = folderConfigs ?? new();
    private FolderParameter? _parameter;
    private BookmarkFolderList? _bookmarks;
    private FolderTreeModel? _folderTree;
    private bool _quickSubscribed;
    private QuickAccessTreeNode? _quickPlace;
    /// <summary>普通树与列表共用来源替换点，但各自保持选择和展开状态；创建不枚举。</summary>
    public FolderTreeModel FolderTree => _folderTree ??= new(archives, this, state: state);
    public bool IsQuickAccessPlace => Place?.StartsWith("quickaccess:", StringComparison.Ordinal) == true;
    public Func<string, Task>? OpenTargetAsync { get; set; }
    public bool IsBookmarkPlace => Place?.StartsWith("bookmark:", StringComparison.Ordinal) == true;
    public BookmarkNode? BookmarkPlace => IsBookmarkPlace ? _bookmarks?.Place : null;
    /// <summary>两个列表共享节点及目录参数，位置与选择各自独立；只订阅真实书签事务。</summary>
    private BookmarkFolderList BookmarkList
    {
        get
        {
            if (_bookmarks is not null) return _bookmarks;
            if (state is null) throw new NotSupportedException("当前书架未接入书签集合。");
            state.BookmarksChanged += BookmarksChanged;
            return _bookmarks = new(state.Bookmarks, _folderConfigs) { ReadMetadataAsync = archives.GetFileMetadataAsync };
        }
    }
    public string? Place { get; private set; }
    public IReadOnlyList<FolderItem> Items { get; private set; } = [];
    public FolderItem? SelectedItem { get; private set; }
    public FolderOrder FolderOrder { get; private set; } = FolderOrder.FileName;
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? Changed;

    /// <summary>快速访问目录进入原书架，文件进入唯一BookOperation；来源判断不进入视图。</summary>
    public async Task<bool> NavigateTargetAsync(string path, CancellationToken token = default)
    {
        if (path.StartsWith("quickaccess:", StringComparison.Ordinal) || path.StartsWith("bookmark:", StringComparison.Ordinal)) return await SetPlaceAsync(path, token: token);
        var item = await archives.GetFileMetadataAsync(path, token);
        if (item?.IsDirectory == true) return await SetPlaceAsync(path, token: token);
        if (item is null) throw new IOException("快速访问目标已不存在。");
        if (OpenTargetAsync is null) throw new NotSupportedException("当前宿主没有书籍打开入口。");
        await OpenTargetAsync(path); return true;
    }

    /// <summary>选择必须属于当前集合，原系统项尚未进入普通书架集合。</summary>
    public void Select(FolderItem? item)
    {
        if (_disposed || (item is not null && !Items.Contains(item))) return;
        SelectedItem = item; Changed?.Invoke(this, EventArgs.Empty);
        if (IsBookmarkPlace) BookmarkList.Select(item?.Bookmark);
    }

    /// <summary>原同步以书籍来源的父目录为位置，并选择该书籍；同目录只更新选择。</summary>
    /// <param name="book">当前已提交书籍。</param>
    /// <param name="token">窗口或导航请求取消。</param>
    /// <param name="force">手动同步时重新枚举。</param>
    public Task<bool> SyncAsync(Book book, CancellationToken token = default, bool force = false, bool fileSystem = true)
    {
        if (!fileSystem && IsBookmarkPlace)
        {
            var target = SelectedItem?.Path == book.Path ? SelectedItem : Items.FirstOrDefault(e => e.Path == book.Path);
            if (target is not null) Select(target);
            return Task.FromResult(true);
        }
        var path = System.IO.Path.TrimEndingDirectorySeparator(book.Path);
        var parent = book.BookAddress.Place ?? path;
        return SetPlaceAsync(parent, book.Path, token, force);
    }

    /// <summary>后台建立列表，成功才替换位置/集合；取消、失败或晚到结果保留原列表和选择。</summary>
    /// <param name="place">真实目录路径，不包含归档内部地址。</param>
    /// <param name="selectedPath">希望定位的真实书籍路径。</param>
    /// <param name="token">调用方取消。</param>
    /// <param name="force">同目录是否重新枚举。</param>
    /// <returns>此次请求是否成功提交。</returns>
    public async Task<bool> SetPlaceAsync(string place, string? selectedPath = null, CancellationToken token = default, bool force = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
        bool bookmark = place.StartsWith("bookmark:", StringComparison.Ordinal);
        bool quick = place.StartsWith("quickaccess:", StringComparison.Ordinal);
        if (!bookmark && !quick) place = System.IO.Path.GetFullPath(place);
        var revision = Interlocked.Increment(ref _revision);
        _request?.Cancel(); var pending = CancellationTokenSource.CreateLinkedTokenSource(token); _request = pending;
        var oldPlace = _bookmarks?.Place ?? (bookmark ? state?.BookmarkRoot : null); var oldSelection = _bookmarks?.SelectedItem; bool committed = false;
        try
        {
            if (!force && Place == place)
            {
                if (selectedPath is not null && SelectedItem?.Path != selectedPath) Select(FindSelection(selectedPath));
                committed = true; Error = null; IsLoading = false; Changed?.Invoke(this, EventArgs.Empty); return true;
            }
            IsLoading = true; Error = null; Changed?.Invoke(this, EventArgs.Empty);
            if (quick)
            {
                var collection = GetQuickAccess();
                var folder = collection.FindNode(place) ?? throw new IOException("快速访问位置已不存在。");
                if (folder.Children is null) throw new IOException("请选择快速访问文件夹。");
                _quickPlace = folder;
                Place = place; Items = folder.Children.Select(node => new FolderItem(node.DisplayName, node.IsFolder ? collection.GetPath(node) : node.Path ?? "", node.IsFolder) { QuickAccess = node }).ToArray();
                SelectedItem = Items.FirstOrDefault(item => item.Path == selectedPath); IsLoading = false; Error = null; committed = true; Changed?.Invoke(this, EventArgs.Empty); return true;
            }
            if (bookmark)
            {
                BookmarkList.Refresh();
                if (force) BookmarkList.InvalidateMetadata();
                var folder = BookmarkList.FindFolder(place) ?? throw new IOException("书签文件夹已不存在。");
                var selected = oldSelection is not null && folder.Children?.Contains(oldSelection) == true && BookmarkList.GetTargetPath(oldSelection) == selectedPath
                    ? oldSelection : folder.Children?.FirstOrDefault(node => BookmarkList.GetTargetPath(node) == selectedPath);
                BookmarkList.SetPlace(folder, selected);
                await BookmarkList.LoadMetadataAsync(pending.Token); pending.Token.ThrowIfCancellationRequested();
                if (_disposed || revision != _revision) return false;
                PublishBookmarks(); committed = true; return true;
            }
            var entries = await archives.ListBooksAsync(place, pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (_disposed || revision != _revision) return false;
            var parameter = new FolderParameter(place, _folderConfigs);
            var mode = GetNormalOrder(parameter.FolderOrder);
            var items = FolderCollection.Sort(entries, mode, Config.Current.Bookshelf.FolderSortOrder, parameter.Seed, pending.Token);
            _entries = entries; _parameter = parameter; Place = place; Items = items; FolderOrder = mode;
            SelectedItem = selectedPath is null ? null : FindSelection(selectedPath); return true;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return false; }
        catch (Exception ex) { if (revision == _revision) Error = "目录暂不可访问：" + ex.Message; return false; }
        finally
        {
            if (bookmark && !committed && revision == _revision && oldPlace is not null) _bookmarks?.SetPlace(oldPlace, oldSelection);
            if (revision == _revision) { IsLoading = false; Changed?.Invoke(this, EventArgs.Empty); }
            if (ReferenceEquals(_request, pending)) _request = null; pending.Dispose();
        }
    }

    /// <summary>原普通列表不提供路径/注册时间排序，保留原字段而使用文件名回退。</summary>
    private static FolderOrder GetNormalOrder(FolderOrder mode) => !Enum.IsDefined(mode) || mode.IsEntryCategory() || mode.IsPathCategory() ? FolderOrder.FileName : mode;
    /// <summary>原归档递归模式同步到根书项，先精确定位，再匹配带目录边界的真实祖先。</summary>
    private FolderItem? FindSelection(string path) => Items.FirstOrDefault(e => e.Path == path)
        ?? Items.FirstOrDefault(e => path.StartsWith(e.Path.TrimEnd('/') + "/", StringComparison.Ordinal));

    /// <summary>在已有元数据上重排并按路径保持选择，不重复扫描或打开书籍。</summary>
    /// <param name="mode">普通书架排序。</param>
    /// <param name="reshuffle">用户重新选择随机时生成新种子；应用其他全局设置时保留随机次序。</param>
    public void ChangeOrder(FolderOrder mode, bool reshuffle = true)
    {
        if (_disposed || IsLoading) return;
        if (IsQuickAccessPlace) return;
        if (IsBookmarkPlace) { BookmarkList.ChangeOrder(mode); PublishBookmarks(); return; }
        if (_disposed || IsLoading || _parameter is null) return;
        mode = GetNormalOrder(mode);
        if (mode != FolderOrder.Random || reshuffle || _parameter.FolderOrder != mode) _parameter.FolderOrder = mode;
        FolderOrder = mode; Reorder();
    }
    /// <summary>失败回滚或全局默认修改后重新恢复原路径参数，不枚举来源。</summary>
    public void ReloadParameter()
    {
        if (_disposed || IsLoading || Place is null) return;
        if (IsQuickAccessPlace) return;
        if (IsBookmarkPlace) { BookmarkList.Refresh(); PublishBookmarks(); return; }
        _parameter = new(Place, _folderConfigs); FolderOrder = GetNormalOrder(_parameter.FolderOrder); Reorder();
    }
    /// <summary>应用全局目录分组时保持随机种子及未支持的原默认排序字段，只调整已提交集合。</summary>
    public void Reorder()
    {
        if (_disposed || IsLoading) return;
        if (IsQuickAccessPlace) return;
        if (IsBookmarkPlace) { BookmarkList.Refresh(); PublishBookmarks(); return; }
        var path = SelectedItem?.Path;
        Items = FolderCollection.Sort(_entries, FolderOrder, Config.Current.Bookshelf.FolderSortOrder, _parameter?.Seed ?? 0, CancellationToken.None);
        SelectedItem = Items.FirstOrDefault(e => e.Path == path); Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>原 GetFolderItem 算法：普通顺序越界停止；随机顺序才循环。</summary>
    /// <param name="offset">前一本 -1、后一本 +1。</param>
    /// <returns>目标条目，边界或无选择时为空。</returns>
    public FolderItem? GetFolderItem(int offset)
    {
        if (_disposed || IsLoading || SelectedItem is null) return null;
        int index = Items.ToList().IndexOf(SelectedItem); if (index < 0) return null;
        int next = FolderOrder == FolderOrder.Random ? (index + Items.Count + offset) % Items.Count : index + offset;
        return next >= 0 && next < Items.Count ? Items[next] : null;
    }

    /// <summary>书架上一级只改变列表，保留原选中目录为定位目标。</summary>
    public Task<bool> UpAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (IsQuickAccessPlace && state?.QuickAccess.FindNode(Place!) is { } current)
        { var quickParent = state.QuickAccess.ParentOf(current); return quickParent is null ? Task.FromResult(false) : SetPlaceAsync(state.QuickAccess.GetPath(quickParent), Place, token); }
        if (IsBookmarkPlace)
        {
            if (!BookmarkList.MoveToParent()) return Task.FromResult(false);
            return SetPlaceAsync(BookmarkList.ParameterPath, BookmarkList.SelectedItem is { } node ? BookmarkList.GetTargetPath(node) : null, token, true);
        }
        return Place is { } place && System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(place)) is { } parent
            ? SetPlaceAsync(parent, place, token) : Task.FromResult(false);
    }
    /// <summary>进入选中目录只改变列表，归档通过打开命令加载。</summary>
    public Task<bool> EnterAsync(CancellationToken token = default) => SelectedItem is { IsDirectory: true } item
        ? SetPlaceAsync(item.Path, token: token) : Task.FromResult(false);
    /// <summary>手动刷新保持真实选中路径，失败保留旧可用列表。</summary>
    public Task<bool> RefreshAsync(CancellationToken token = default) => Place is { } place
        ? SetPlaceAsync(place, SelectedItem?.Path, token, true) : Task.FromResult(false);
    /// <summary>窗口关闭取消枚举；后端晚到结果不能更新已关闭的集合。</summary>
    public void Dispose() { _disposed = true; Interlocked.Increment(ref _revision); _request?.Cancel(); _folderTree?.Dispose(); _bookmarks?.Dispose(); if (state is not null) { state.BookmarksChanged -= BookmarksChanged; state.QuickAccessChanged -= QuickAccessChanged; } }
    private QuickAccessCollection GetQuickAccess()
    {
        if (state is null) throw new NotSupportedException("快速访问未接入。");
        if (!_quickSubscribed) { state.QuickAccessChanged += QuickAccessChanged; _quickSubscribed = true; }
        return state.QuickAccess;
    }
    /// <summary>快速访问事务完成才更新同一书架；重命名按原节点恢复位置，不扫描目录。</summary>
    private void QuickAccessChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsQuickAccessPlace || state is null) return;
        var collection = state.QuickAccess; var selected = SelectedItem?.QuickAccess;
        var folder = _quickPlace is not null && collection.Root.Walk().Contains(_quickPlace) ? _quickPlace : collection.Root;
        _quickPlace = folder; Place = collection.GetPath(folder);
        Items = (folder.Children ?? []).Select(node => new FolderItem(node.DisplayName, node.IsFolder ? collection.GetPath(node) : node.Path ?? "", node.IsFolder) { QuickAccess = node }).ToArray();
        SelectedItem = Items.FirstOrDefault(item => ReferenceEquals(item.QuickAccess, selected)); Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>列表重建保持原节点选择，普通目标Path只用于加载/历史，不作为别名身份。</summary>
    private void PublishBookmarks()
    {
        Place = BookmarkList.ParameterPath; FolderOrder = BookmarkList.FolderOrder;
        Items = BookmarkList.Items.Select(node => new FolderItem(node.DisplayName, BookmarkList.GetTargetPath(node), node.IsFolder) { Bookmark = node }).ToArray();
        SelectedItem = Items.FirstOrDefault(item => ReferenceEquals(item.Bookmark, BookmarkList.SelectedItem));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>书签事务提交/回滚后同步当前位置；阅读保存不重枚举。</summary>
    private void BookmarksChanged(object? sender, EventArgs e)
    {
        if (_disposed || !IsBookmarkPlace) return;
        if (IsLoading) _request?.Cancel();
        BookmarkList.Refresh();
        if (BookmarkList.FindFolder(Place!) is { } folder) BookmarkList.SetPlace(folder, SelectedItem?.Bookmark);
        PublishBookmarks();
    }
    /// <summary>元数据排序的异步补齐，调用者观察错误；不打开书籍或图片。</summary>
    public async Task RefreshBookmarkMetadataAsync(CancellationToken token = default)
    { if (IsBookmarkPlace && await BookmarkList.LoadMetadataAsync(token)) PublishBookmarks(); }
}
