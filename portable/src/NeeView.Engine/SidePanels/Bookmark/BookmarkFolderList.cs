// Copyright (c) NeeLaboratory. 来源：BookmarkFolderList、BookmarkFolderCollection 和 FolderList 的目录导航/排序子集。
namespace NeeView;

/// <summary>浏览原书签集合，不创建第二棵树；位置和选择仅属于当前窗口。</summary>
public sealed class BookmarkFolderList : IDisposable
{
    private readonly BookmarkCollection _collection;
    private BookmarkNode[] _ancestors = [];
    private readonly Dictionary<BookmarkNode, int> _seeds = [];
    private readonly Dictionary<BookmarkNode, BookmarkNode> _parents = [];
    private CancellationTokenSource? _searchRequest;
    private long _searchRevision;
    private IReadOnlyList<BookmarkNode>? _matches;
    private bool _disposed;
    public string SearchKeyword { get; private set; } = "";
    public bool IsSearching { get; private set; }
    public bool HasHistoryPredicate => SearchBookmarkFolderCollection.Analyze(SearchKeyword).Any(key => key.Property.Name == "history");
    public BookmarkNode Place { get; private set; }
    public BookmarkNode? SelectedItem { get; private set; }
    public IReadOnlyList<BookmarkNode> Items { get; private set; } = [];
    public FolderOrder FolderOrder { get; private set; }
    public string FullPath { get; private set; } = "书签";
    public bool CanMoveToParent => !ReferenceEquals(Place, _collection.Items);
    public string? CapabilityMessage => SupportsOrder(Config.Current.Bookmark.BookmarkFolderOrder) ? null : "此排序需要来源元数据，尚未迁移；当前按文件名显示，原配置保留。";

    /// <summary>接收已加载的原集合；不读取文件、不持有窗口或图像资源。</summary>
    /// <param name="collection">由 SaveData 维护的唯一书签集合。</param>
    public BookmarkFolderList(BookmarkCollection collection)
    {
        _collection = collection; Place = collection.Items; Refresh();
    }

    /// <summary>本批开放不需文件系统探测的原排序，时间/大小排序仍为能力占位。</summary>
    public static bool SupportsOrder(FolderOrder mode) => Enum.IsDefined(mode) && mode is not
        (FolderOrder.TimeStamp or FolderOrder.TimeStampDescending or FolderOrder.Size or FolderOrder.SizeDescending);

    /// <summary>选择必须属于当前列表；仅改变导航对象，不打开书籍。</summary>
    public void Select(BookmarkNode? node) => SelectedItem = node is not null && Items.Contains(node) ? node : null;

    /// <summary>进入当前树中的文件夹；返回时通过原节点引用定位，不依赖排序下标。</summary>
    /// <param name="folder">原书签根或文件夹。</param><param name="selected">可选的子项定位。</param>
    /// <returns>是否进入有效位置；过期目标保留旧位置。</returns>
    public bool SetPlace(BookmarkNode folder, BookmarkNode? selected = null)
    {
        if (!folder.IsFolder || !_collection.Items.Walk().Contains(folder)) return false;
        CancelSearch(); SearchKeyword = ""; _matches = null;
        Place = folder; SelectedItem = selected; Refresh(); return true;
    }

    /// <summary>原 MoveToParent：只在书签范围内返回，并选中刚离开的目录。</summary>
    /// <returns>根位置返回 false，不跨入普通文件系统。</returns>
    public bool MoveToParent() => _collection.ParentOf(Place) is { } parent && SetPlace(parent, Place);

    /// <summary>原固定 Home 为书签根；保留原集合根引用。</summary>
    public void MoveToRoot() => SetPlace(_collection.Items);

    /// <summary>编辑成功后显示实际保留节点的父级；登记/合并/跨级移动共用此定位。</summary>
    public bool Reveal(BookmarkNode node) => _collection.ParentOf(node) is { } parent && SetPlace(parent, node);

    /// <summary>原 SyncBookAsync 优先当前目录中的同路径项，再按树顺序查找；没有书签只刷新当前位置。</summary>
    /// <param name="path">当前已提交书籍的原来源路径。</param><returns>是否找到并定位。</returns>
    public bool Sync(string? path)
    {
        var node = path is null ? null : Place.Children?.FirstOrDefault(e => !e.IsFolder && e.Path == path)
            ?? _collection.Items.Walk().FirstOrDefault(e => !e.IsFolder && e.Path == path);
        if (node is null) { Refresh(); return false; }
        return Reveal(node);
    }

    /// <summary>采用原默认排序字段；不支持的选择不覆盖旧配置。</summary>
    public void ChangeOrder(FolderOrder mode)
    {
        if (!SupportsOrder(mode)) throw new NotSupportedException("此书签排序尚未迁移。");
        Config.Current.Bookmark.BookmarkFolderOrder = mode;
        if (mode == FolderOrder.Random) _seeds[Place] = Random.Shared.Next(1, int.MaxValue);
        Refresh();
    }

    /// <summary>集合提交或回滚后刷新；目录失效退到最近存活祖先，仍在树中的移动目录继续保留。</summary>
    public void Refresh()
    {
        // 节点/顺序变化使后台快照过期；调用方按已提交搜索重新申请，不扫描磁盘。
        CancelSearch();
        var live = _collection.Items.Walk().ToHashSet();
        // 从同一批节点生成派生父级索引；路径比较不在每次比较中重新遍历整棵树。
        _parents.Clear();
        foreach (var parent in live.Where(node => node.IsFolder))
            foreach (var child in parent.Children!) _parents[child] = parent;
        if (!live.Contains(Place)) { Place = _ancestors.LastOrDefault(live.Contains) ?? _collection.Items; SearchKeyword = ""; _matches = null; }
        var chain = new Stack<BookmarkNode>();
        for (BookmarkNode? parent = Place; parent is not null; parent = _parents.GetValueOrDefault(parent)) chain.Push(parent);
        _ancestors = chain.ToArray();
        FullPath = "书签" + string.Concat(_ancestors.Skip(1).Select(node => " / " + node.DisplayName));
        foreach (var dead in _seeds.Keys.Where(node => !live.Contains(node)).ToArray()) _seeds.Remove(dead);
        FolderOrder = SupportsOrder(Config.Current.Bookmark.BookmarkFolderOrder) ? Config.Current.Bookmark.BookmarkFolderOrder : FolderOrder.FileName;
        if (!_seeds.TryGetValue(Place, out var seed)) _seeds[Place] = seed = Random.Shared.Next(1, int.MaxValue);
        Items = Sort(_matches is null ? Place.Children ?? [] : _matches.Where(live.Contains), FolderOrder, Config.Current.Bookshelf.FolderSortOrder, seed);
        if (SelectedItem is not null && !Items.Contains(SelectedItem)) SelectedItem = null;
    }

    /// <summary>异步搜索当前原节点范围；成功才替换表达式和结果，错误/取消保留旧列表。</summary>
    /// <param name="keyword">Trim 后的原表达式；空串回到当前目录普通子项。</param>
    /// <param name="historyPaths">原访问历史路径的调用时快照。</param><param name="metadata">后端元数据替换点。</param>
    /// <param name="token">输入变更或关闭取消。</param><returns>是否提交到仍然有效的位置。</returns>
    public async Task<bool> SearchAsync(string keyword, IEnumerable<string> historyPaths,
        Func<string, CancellationToken, Task<FolderItem?>>? metadata = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); CancelSearch(); token.ThrowIfCancellationRequested();
        keyword = keyword.Trim();
        if (keyword.Length == 0) { SearchKeyword = ""; _matches = null; Refresh(); return true; }
        var pending = CancellationTokenSource.CreateLinkedTokenSource(token); _searchRequest = pending;
        var revision = _searchRevision; var place = Place; var paths = historyPaths.ToHashSet(StringComparer.Ordinal);
        // 只在调用线程读取集合/名称；后台不可遍历正在编辑的 ObservableCollection。
        var source = Config.Current.Bookmark.IsSearchIncludeSubdirectories ? Place.Walk().Skip(1) : Place.Children ?? [];
        var snapshot = source.Where(node => node.IsFolder || !string.IsNullOrWhiteSpace(node.Path)).Select(node =>
            new BookmarkSearchItem(node, node.DisplayName, node.Path, node.IsFolder, node.EntryTime, node.Path is { } path && paths.Contains(path))).ToArray();
        IsSearching = true;
        try
        {
            var found = await Task.Run(() => SearchBookmarkFolderCollection.SearchAsync(keyword, snapshot, metadata, pending.Token), pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (_disposed || revision != _searchRevision || !ReferenceEquals(place, Place)) return false;
            SearchKeyword = keyword; _matches = found; Refresh(); return true;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_searchRequest, pending)) { _searchRequest = null; IsSearching = false; }
            pending.Dispose();
        }
    }

    /// <summary>替换/导航/关闭使晚到查询失效；不改已经提交的列表。</summary>
    public void CancelSearch()
    {
        _searchRevision++; _searchRequest?.Cancel(); _searchRequest = null; IsSearching = false;
    }

    /// <summary>面板释放只取消查询，唯一书签集合仍由 SaveData 维护。</summary>
    public void Dispose() { _disposed = true; CancelSearch(); }

    /// <summary>保留原注册顺序不受目录分组影响；其他模式先分组再比较，随机种子在当前位置稳定。</summary>
    private IReadOnlyList<BookmarkNode> Sort(IEnumerable<BookmarkNode> source, FolderOrder mode, FolderSortOrder grouping, int seed)
    {
        var nodes = source.ToArray();
        // 原 Directory/File 的 ConstOrder 同为 2；EntryTime 只按节点索引，降序反转整个普通项目序列。
        // 原 GetIndex 是父节点中的索引；递归搜索跨父级的同索引按树枚举顺序稳定保留。
        if (mode == FolderOrder.EntryTime) return nodes.OrderBy(node => _parents[node].Children!.IndexOf(node)).ToArray();
        if (mode == FolderOrder.EntryTimeDescending) return nodes.OrderBy(node => _parents[node].Children!.IndexOf(node)).Reverse().ToArray();
        var order = nodes.OrderBy(_ => 0);
        order = grouping switch
        {
            FolderSortOrder.First => order.ThenBy(node => node.IsFolder ? 0 : 1),
            FolderSortOrder.Last => order.ThenByDescending(node => node.IsFolder ? 0 : 1),
            _ => order
        };
        var byName = Comparer<BookmarkNode>.Create((x, y) => NaturalSort.Compare(x.DisplayName, y.DisplayName));
        var paths = mode.IsPathCategory() ? nodes.ToDictionary(node => node, GetTargetPath) : [];
        var byPath = Comparer<BookmarkNode>.Create((x, y) => NaturalSort.Compare(paths[x], paths[y]));
        var byType = Comparer<BookmarkNode>.Create((x, y) =>
        {
            if (x.IsFolder) return y.IsFolder ? byName.Compare(x, y) : 1;
            if (y.IsFolder) return -1;
            var a = System.IO.Path.GetExtension(x.DisplayName); var b = System.IO.Path.GetExtension(y.DisplayName);
            return a != b ? NaturalSort.Compare(a, b) : byName.Compare(x, y);
        });
        var random = new Random(seed);
        return (mode switch
        {
            FolderOrder.FileNameDescending => order.ThenByDescending(node => node, byName),
            FolderOrder.Path => order.ThenBy(node => node, byPath),
            FolderOrder.PathDescending => order.ThenByDescending(node => node, byPath),
            FolderOrder.FileType => order.ThenBy(node => node, byType),
            FolderOrder.FileTypeDescending => order.ThenByDescending(node => node, byType),
            FolderOrder.Random => order.ThenBy(_ => random.Next()),
            _ => order.ThenBy(node => node, byName)
        }).ToArray();
    }

    /// <summary>原文件夹目标是 bookmark scheme，书籍目标仍是原 Path；仅用于排序，不访问磁盘。</summary>
    private string GetTargetPath(BookmarkNode node)
    {
        if (!node.IsFolder) return node.Path ?? "";
        var names = new Stack<string>();
        for (var folder = node; folder != _collection.Items; folder = _parents[folder]) names.Push(folder.Name ?? "");
        return "bookmark:" + string.Join('\\', names);
    }
}
