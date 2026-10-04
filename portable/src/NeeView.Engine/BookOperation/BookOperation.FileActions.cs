namespace NeeView;

public sealed partial class BookOperation
{
    private DestinationMoveService? _destinationMoves;
    private IFileOperationBackend? _fileBackend;
    private BitmapFactory? _fileImages;
    private Page? _fileSelection;
    private DestinationFolderPanel? _destinationFolders;
    public DestinationMoveService? DestinationMoves => _destinationMoves;
    public DestinationFolderPanel DestinationFolders => _destinationFolders ??= new(this, archives);
    /// <summary>当前主图片目录仅来自真实普通条目，不能以归档逻辑地址推算文件操作目标。</summary>
    public string? CurrentPictureDirectory => Book?.CurrentPage is { IsImage: true, ArchiveEntry.FilePath: { } path } page && page.ArchiveEntry.Archive.IsDirectory
        ? System.IO.Path.GetDirectoryName(path) : null;
    public Page? FileActionPage => (BrowseMode == BrowseLayoutMode.Masonry ? _fileSelection : Book?.CurrentPage) is { } selected && Book?.Pages.Contains(selected) == true ? selected : null;
    public bool CanFileAction => Config.Current.System.IsFileWriteAccessEnabled && _destinationMoves is not null && !_destinationMoves.IsBusy && !_disposed && !_closing && !IsLoading && Book?.IsIndexing == false
        && FileActionPage is { IsImage: true, ArchiveEntry.FilePath: not null } page && page.ArchiveEntry.Archive.IsDirectory && !page.ArchiveEntry.IsShortcut;

    /// <summary>唯一装配边界；图像失效仍使用原工厂，移动历史在窗口重开时继续共享。</summary>
    public void AttachFileOperations(DestinationMoveService moves, IFileOperationBackend backend, BitmapFactory images)
    { _destinationMoves = moves; _fileBackend = backend; _fileImages = images; }

    /// <summary>明确点击选择分类目标；滚动、悬停和临时滑条选择不能调用此入口。</summary>
    public async Task SelectFileActionPageAsync(Book book, Page page)
    {
        await _gate.WaitAsync();
        try { if (!_disposed && !_closing && ReferenceEquals(Book, book) && book.Pages.Contains(page)) { _fileSelection = page; Notify(); } }
        finally { _gate.Release(); }
    }
    /// <summary>九数字命令按原1-based索引，复制模式只作用于数字/面板命令。</summary>
    public Task ClassifyAsync(int index, bool followPanelMode = true)
    {
        var folders = Config.Current.System.DestinationFolderCollection;
        return index >= 1 && folders.IsValidIndex(index - 1) ? ClassifyAsync(folders[index - 1], followPanelMode && Config.Current.Panels.IsDestinationFolderCopyMode) : Task.CompletedTask;
    }
    public MoveToFolderAsCommandParameter GetDestinationParameter(string name) => saveData.GetDestinationParameter(name);
    /// <summary>原数字命令可配置目标索引，不能将命令后缀误当作永久绑定。</summary>
    public Task ClassifyCommandAsync(string name)
    {
        var parameter = GetDestinationParameter(name);
        if (parameter.MultiPagePolicy != MultiPagePolicy.Once) throw new NotSupportedException("本批分类仅支持原单图策略；多页策略保留待后续迁入。");
        return ClassifyAsync(parameter.Index);
    }
    /// <summary>当前主图片按真实结果更新原集合；取消/失败不翻页，复制不改变阅读位置。</summary>
    public async Task ClassifyAsync(DestinationFolder folder, bool copy, CancellationToken token = default)
    {
        var requestedBook = Book; var requestedPage = FileActionPage;
        await _gate.WaitAsync(token);
        try
        {
            if (!CanFileAction || !ReferenceEquals(requestedBook, Book) || !ReferenceEquals(requestedPage, FileActionPage) || !folder.IsValid()) return;
            _saving?.Cancel();
            var book = Book!; var page = requestedPage!; var readingAnchor = book.CurrentPage;
            var result = await _destinationMoves!.TransferAsync(page.ArchiveEntry.FilePath!, System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(page.ArchiveEntry.FilePath!)), !copy, token);
            Error = _destinationMoves.Error;
            if (result is null) { Notify(); return; }
            // 不重建另一阅读内核；移动成功从原来源集合移除，同Page引用与排序规则继续使用。
            var relative = System.IO.Path.GetRelativePath(book.Path, result.Destination);
            bool withinSource = relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative)
                && (book.Setting.IsRecursiveFolder || !relative.Contains('/'));
            if (!copy || withinSource)
            {
                int previous = page.Index;
                var next = book.Pages.Skip(previous + 1).FirstOrDefault() ?? book.Pages.Take(previous).LastOrDefault();
                var source = book.Pages.SourcePages.Where(p => (copy || !ReferenceEquals(p, page)) && p.ArchiveEntry.FilePath != result.Destination).ToList();
                // 递归书内分类仍属于同一来源；替换实际落点Page使旧目标像素/定位失效，其他Page保持引用。
                if (withinSource)
                {
                    var moved = new Page(new ArchiveEntry(book.Source) { Id = book.Pages.SourcePages.Max(p => p.EntryIndex) + 1, RawEntryName = relative,
                        FilePath = result.Destination, Length = page.ArchiveEntry.Length, LastWriteTime = page.ArchiveEntry.LastWriteTime });
                    moved.Content.PageDataSource = page.Content.PageDataSource; moved.Content.HasSize = page.Content.HasSize; source.Add(moved);
                }
                book.Pages.SetSourcePages(source);
                book.Sort(CancellationToken.None); if (!copy) _fileSelection = null;
                // 空搜索结果保留原查询和可恢复来源锚点，但不能继续操作不可见图片。
                if (book.Pages.Count == 0) book.CurrentPage = book.Pages.SourcePages.FirstOrDefault(p => p.EntryIndex > page.EntryIndex) ?? book.Pages.SourcePages.LastOrDefault();
                var anchor = copy ? readingAnchor : next;
                Position = new(anchor is not null && book.Pages.Contains(anchor) ? anchor.Index : 0, copy ? Position.Part : 0);
                await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory();
            }
            _fileImages?.InvalidateCovers();
            try { await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
            catch (Exception ex) { Error = "文件操作已成功，阅读状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            Notify();
        }
        finally { _gate.Release(); }
    }
    /// <summary>共享历史允许跨书撤销；仍浏览恢复目录才重载定位，否则仅回报真实路径。</summary>
    public async Task ReplayDestinationMoveAsync(bool undo, CancellationToken token = default)
    {
        if (_destinationMoves is null || _destinationMoves.IsBusy || !Config.Current.System.IsFileWriteAccessEnabled) return;
        FileTransferResult? result; Book? book; long generation; string? entry = null; bool reload = false;
        await _gate.WaitAsync(token);
        try
        {
            if (_disposed || _closing || IsLoading) return;
            _saving?.Cancel();
            book = Book; generation = _generation;
            result = await _destinationMoves.ReplayAsync(undo, token); Error = _destinationMoves.Error;
            if (result is null) { Notify(); return; }
            _fileImages?.InvalidateCovers();
            if (book?.Source.IsDirectory == true)
            {
                string relative = System.IO.Path.GetRelativePath(book.Path, result.Destination);
                if (Belongs(relative)) { entry = relative; reload = true; }
                else
                {
                    string sourceRelative = System.IO.Path.GetRelativePath(book.Path, result.Source);
                    if (Belongs(sourceRelative))
                    {
                        reload = true;
                        // 覆盖撤销可能在移出路径恢复原目标；不能把它当作已删除页永久移出索引。
                        if (await _fileBackend!.FileExistsAsync(result.Source, CancellationToken.None)) entry = sourceRelative;
                        else
                        {
                            var page = book.Pages.SourcePages.FirstOrDefault(p => p.ArchiveEntry.FilePath == result.Source);
                            if (page is not null)
                            {
                                int index = page.Index; var next = book.Pages.ElementAtOrDefault(index + 1) ?? book.Pages.ElementAtOrDefault(index - 1);
                                book.Pages.SetSourcePages(book.Pages.SourcePages.Where(p => !ReferenceEquals(p, page))); book.Sort(CancellationToken.None);
                                if (book.Pages.Count == 0) book.CurrentPage = book.Pages.SourcePages.FirstOrDefault(p => p.EntryIndex > page.EntryIndex) ?? book.Pages.SourcePages.LastOrDefault();
                                Position = new(next is not null && book.Pages.Contains(next) ? next.Index : 0, 0); RebuildFrame(1); _fileSelection = null;
                            }
                            entry = book.CurrentPage?.EntryName;
                        }
                    }
                }
                bool Belongs(string path) => path != ".." && !path.StartsWith("../", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(path)
                    && (book.Setting.IsRecursiveFolder || !path.Contains('/'));
            }
            if (!reload) Error ??= "文件已恢复到：" + result.Destination;
            Notify();
        }
        finally { _gate.Release(); }
        // 对话框/文件IO期间另一个打开可以准备来源；过期结果不能切回旧书。
        if (reload && !_closing && !_disposed && generation == _generation && ReferenceEquals(book, Book))
            await OpenCoreAsync(book!.Path, CancellationToken.None, entry, keepHistoryOrder: true, startupMemento: book.CreateMemento(), pageSearchKeyword: book.Pages.SearchKeyword);
    }
    /// <summary>手动集合/开关/分隔比例统一进入原配置事务，失败恢复旧字段。</summary>
    public async Task EditDestinationFoldersAsync(Action edit, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed || _closing, this);
            var panels = Config.Current.Panels;
            var folders = new DestinationFolderCollection(Config.Current.System.DestinationFolderCollection.Select(folder => (DestinationFolder)folder.Clone()));
            bool writeAccess = Config.Current.System.IsFileWriteAccessEnabled;
            var previous = (panels.IsDestinationFolderCopyMode, panels.IsDestinationFolderAutoRefreshEnabled, panels.DestinationFolderSectionRatio, panels.DestinationMoveHistoryCapacity);
            try { edit(); await saveData.SaveAsync(null, token); }
            catch
            {
                Config.Current.System.DestinationFolderCollection = folders;
                Config.Current.System.IsFileWriteAccessEnabled = writeAccess;
                (panels.IsDestinationFolderCopyMode, panels.IsDestinationFolderAutoRefreshEnabled, panels.DestinationFolderSectionRatio, panels.DestinationMoveHistoryCapacity) = previous;
                DestinationFolders.RefreshManaged(); Notify();
                throw;
            }
            if (_destinationMoves is not null) await _destinationMoves.ApplyHistoryCapacityAsync();
            DestinationFolders.RefreshManaged(); Notify();
        }
        finally { _gate.Release(); }
    }
    /// <summary>新建直接子目录复用后端，不由视图写文件；成功后手动刷新当前下区。</summary>
    public async Task CreateDestinationChildAsync(string name, string? expectedDirectory, CancellationToken token = default)
    {
        if (_fileBackend is null || expectedDirectory is null || expectedDirectory != CurrentPictureDirectory || _closing || _disposed) return;
        await _fileBackend.CreateDirectoryAsync(expectedDirectory, name, token);
        await DestinationFolders.RefreshChildrenAsync(token);
    }
}
