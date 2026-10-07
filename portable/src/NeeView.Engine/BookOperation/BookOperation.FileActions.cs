namespace NeeView;

public sealed partial class BookOperation
{
    private DestinationMoveService? _destinationMoves;
    private IFileOperationBackend? _fileBackend;
    private BitmapFactory? _fileImages;
    private Page? _fileSelection;
    private DestinationFolderPanel? _destinationFolders;
    private readonly object _fileCopySync = new();
    private CancellationTokenSource? _fileCopyPreparation;
    public DestinationMoveService? DestinationMoves => _destinationMoves;
    public DestinationFolderPanel DestinationFolders => _destinationFolders ??= new(this, archives);
    /// <summary>当前主图片目录仅来自真实普通条目，不能以归档逻辑地址推算文件操作目标。</summary>
    public string? CurrentPictureDirectory => Book?.CurrentPage is { IsImage: true, ArchiveEntry.FilePath: { } path } page && page.ArchiveEntry.Archive.IsDirectory
        ? System.IO.Path.GetDirectoryName(path) : null;
    public Page? FileActionPage => (BrowseMode == BrowseLayoutMode.Masonry ? _fileSelection : Book?.CurrentPage) is { } selected && Book?.Pages.Contains(selected) == true ? selected : null;
    public bool CanFileAction => CanTransferFileActionPages(MultiPagePolicy.Once, requireWriteAccess: true);
    /// <summary>切书/退出取消尚未生成真实请求的归档提取，已提交文件复制仍协调真实结果。</summary>
    public void CancelFileCopyPreparation()
    { lock (_fileCopySync) _fileCopyPreparation?.Cancel(); }

    /// <summary>读取原当前页集合；分页按阅读范围，瀑布仅使用显式选中图片，不扩大到可见区。</summary>
    /// <param name="policy">Once/All/AllLeftToRight沿原CollectPages判断顺序。</param>
    /// <returns>来自当前可读集合的原Page，重复分割页仅返回一次。</returns>
    public IReadOnlyList<Page> CollectFileActionPages(MultiPagePolicy policy)
    {
        IEnumerable<Page> pages = IsFrameReading ? Book?.CurrentPages ?? [] : FileActionPage is { } selected ? [selected] : [];
        pages = pages.Where(page => Book?.Pages.Contains(page) == true).Distinct();
        pages = policy switch
        {
            MultiPagePolicy.Once => pages.Take(1),
            MultiPagePolicy.All => pages,
            MultiPagePolicy.AllLeftToRight => Book?.Setting.BookReadOrder == PageReadOrder.RightToLeft ? pages.Reverse() : pages,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        return pages.ToArray();
    }
    /// <summary>原移动要求整组均为普通目录真实图片；固定复制不受源写权限开关限制。</summary>
    public bool CanTransferFileActionPages(MultiPagePolicy policy, bool requireWriteAccess = true)
    {
        if (!Enum.IsDefined(policy)) return false;
        if (!requireWriteAccess) return CanCopyToFolder(policy);
        if (requireWriteAccess && !Config.Current.System.IsFileWriteAccessEnabled || IsUsingClipboard || IsRenamingBook || IsTransferringBook || IsDeletingFile || _destinationMoves is null || _destinationMoves.IsBusy || _disposed || _closing || IsLoading || Book?.IsIndexing != false) return false;
        var pages = CollectFileActionPages(policy);
        return pages.Count > 0 && pages.All(page => page is { IsImage: true, ArchiveEntry.FilePath: not null } && page.ArchiveEntry.Archive.IsDirectory);
    }
    /// <summary>原固定复制允许文件和目录页；实体目录复用整树快照后端，数字分类仍限普通图片。</summary>
    /// <param name="policy">原页组范围。</param><returns>整组具备文件复制能力时为 true。</returns>
    public bool CanCopyToFolder(MultiPagePolicy policy = MultiPagePolicy.Once)
    {
        if (!Enum.IsDefined(policy) || IsUsingClipboard || IsRenamingBook || IsTransferringBook || IsDeletingFile || _destinationMoves is null || _destinationMoves.IsBusy || _disposed || _closing || IsLoading || Book?.IsIndexing != false) return false;
        var pages = CollectFileActionPages(policy);
        return pages.Count > 0 && pages.All(page => CanCopyEntry(page)
            && (page.ArchiveEntry.TargetArchiveEntry is not { IsDirectory: true, FilePath: not null } || _fileBackend is IBookTransferBackend));
    }

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
    public Task ClassifyAsync(int index, bool followPanelMode = true, MultiPagePolicy policy = MultiPagePolicy.Once)
    {
        var folders = Config.Current.System.DestinationFolderCollection;
        return index >= 1 && folders.IsValidIndex(index - 1) ? ClassifyAsync(folders[index - 1], followPanelMode && Config.Current.Panels.IsDestinationFolderCopyMode, policy: policy) : Task.CompletedTask;
    }
    public MoveToFolderAsCommandParameter GetDestinationParameter(string name) => saveData.GetDestinationParameter(name);
    /// <summary>原数字命令可配置目标索引和多页策略，不能将命令后缀误当作永久绑定。</summary>
    public Task ClassifyCommandAsync(string name)
    {
        var parameter = GetDestinationParameter(name);
        return ClassifyAsync(parameter.Index, policy: parameter.MultiPagePolicy);
    }
    /// <summary>当前阅读范围按真实成功项更新；取消/失败不推进未成功项，复制不改变位置。</summary>
    public Task ClassifyAsync(DestinationFolder folder, bool copy, CancellationToken token = default, MultiPagePolicy policy = MultiPagePolicy.Once)
        => TransferFileActionPagesAsync(folder, copy, policy, requireWriteAccess: true, token);
    /// <summary>原CopyToFolderAs固定复制，不跟随面板模式，也不将复制纳入移动历史。</summary>
    public Task CopyToFolderAsync(DestinationFolder folder, MultiPagePolicy policy = MultiPagePolicy.Once, CancellationToken token = default)
        => TransferFileActionPagesAsync(folder, copy: true, policy, requireWriteAccess: false, token);

    private async Task TransferFileActionPagesAsync(DestinationFolder folder, bool copy, MultiPagePolicy policy, bool requireWriteAccess, CancellationToken token)
    {
        if (copy && !requireWriteAccess) { await CopyRealizedPagesToFolderAsync(folder, policy, token); return; }
        var requestedBook = Book; var requestedPages = CollectFileActionPages(policy);
        await _gate.WaitAsync(token);
        try
        {
            if (!CanTransferFileActionPages(policy, requireWriteAccess) || !ReferenceEquals(requestedBook, Book) || !requestedPages.SequenceEqual(CollectFileActionPages(policy)) || !folder.IsValid()) return;
            var pages = requestedPages.GroupBy(page => System.IO.Path.GetFullPath(page.ArchiveEntry.FilePath!), StringComparer.Ordinal).Select(group => group.First()).ToArray();
            // 开始前核对整组；混合不可操作页或已不存在的源不能静默降级为部分选区。
            foreach (var page in pages)
            {
                if (!await _fileBackend!.FileExistsAsync(page.ArchiveEntry.FilePath!, token)) { Error = "源图片已不存在：" + page.EntryName; Notify(); return; }
            }
            _saving?.Cancel();
            var book = Book!; var readingAnchor = book.CurrentPage;
            var results = await _destinationMoves!.TransferManyAsync(pages.Select(page => new FileTransferRequest(page.ArchiveEntry.FilePath!,
                System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(page.ArchiveEntry.FilePath!)), !copy, PreserveSourceLink: page.ArchiveEntry.IsShortcut)).ToArray(), token);
            Error = _destinationMoves.Error;
            if (results.Count == 0) { Notify(); return; }
            bool changed = false;
            foreach (var result in results)
            {
                var page = pages.First(page => System.IO.Path.GetFullPath(page.ArchiveEntry.FilePath!) == result.Source);
                changed |= await ApplyTransferredPageAsync(book, page, result, copy, readingAnchor);
            }
            // 一次批次只补尺寸/重建正文一次；晚取消仍提交真实成功项并保持失败项。
            if (changed) { await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory(); }
            _fileImages?.InvalidateCovers();
            try { await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
            catch (Exception ex) { Error = "文件操作已成功，阅读状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            Notify();
        }
        finally { _gate.Release(); }
    }
    /// <summary>沿原ArchiveEntry实体化链准备整组，再复用真实文件复制/覆盖协议；不修改归档来源。</summary>
    /// <param name="folder">原配置目标目录。</param><param name="policy">原Once/All/AllLeftToRight选序。</param>
    /// <param name="token">准备/提交前取消，已提交项仍协调实际结果。</param><returns>复制批次完成任务。</returns>
    private async Task CopyRealizedPagesToFolderAsync(DestinationFolder folder, MultiPagePolicy policy, CancellationToken token)
    {
        var requestedBook = Book; var pages = CollectFileActionPages(policy); var generation = _generation;
        var archivePolicy = Config.Current.System.ArchiveCopyPolicy.LimitedRealization();
        RealizedFilePathList? realized = null;
        BookMemento? refresh = null; string? refreshSearch = null, failure = null; int refreshPart = 0;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
        await _gate.WaitAsync(token);
        try
        {
            if (!CanCopyToFolder(policy) || !ReferenceEquals(requestedBook, Book) || generation != _generation || !pages.SequenceEqual(CollectFileActionPages(policy)) || !folder.IsValid()) return;
            var book = Book!; var readingAnchor = book.CurrentPage;
            _saving?.Cancel();
            var directoryTargets = new HashSet<string>(StringComparer.Ordinal);
            string? physicalBook = null;
            // 确认和提交前仍可由切书/关闭取消；实体已成功的结果继续协调，不丢弃前项。
            lock (_fileCopySync) _fileCopyPreparation = pending;
            var results = await _destinationMoves!.TransferManyAsync(async preparationToken =>
            {
                foreach (var entry in pages.Select(page => page.ArchiveEntry.TargetArchiveEntry).Distinct())
                {
                    var path = entry.FilePath ?? entry.Archive.RootArchivePath;
                    var info = await archives.GetFileMetadataAsync(path, preparationToken);
                    if (info is null) throw new FileNotFoundException("复制来源已不存在。", path);
                    if (info.IsSymbolicLink != (entry.FilePath is not null && entry.IsShortcut || entry.Archive.IsRootShortcut)) throw new IOException("复制来源链接类型已改变，请重新加载。");
                    if (entry.FilePath is not null && info.IsDirectory != entry.IsDirectory) throw new IOException("来源类型已改变，请重新操作。");
                }
                realized = await ArchiveEntryUtility.RealizeArchiveEntry(pages.Select(page => page.ArchiveEntry), archivePolicy, _entryRealizer, preparationToken);
                var requests = new List<FileTransferRequest>();
                foreach (var path in realized.Paths)
                {
                    var info = await archives.GetFileMetadataAsync(path, preparationToken) ?? throw new FileNotFoundException("复制实体已不存在。", path);
                    if (info.IsSymbolicLink != pages.Any(p => p.ArchiveEntry.TargetArchiveEntry.FilePath == path && p.ArchiveEntry.TargetArchiveEntry.IsShortcut)) throw new IOException("复制来源链接类型已改变，请重新加载。");
                    if (info.IsDirectory && !info.IsSymbolicLink)
                    {
                        await ProtectBookTransferDestinationAsync(path, preparationToken);
                        var plan = await ((IBookTransferBackend)_fileBackend!).PlanBookTransferAsync(path, folder.Path, preparationToken);
                        if (!plan.Target.IsDirectory) throw new IOException("目录来源类型已改变，请重新操作。");
                        await ProtectBookTransferDestinationAsync(plan.Destination, preparationToken);
                        directoryTargets.Add(await archives.GetPhysicalPathAsync(plan.Destination, preparationToken));
                        requests.Add(new(plan.Target.Path, plan.Destination, false, DirectoryCopyPlan: plan));
                    }
                    else requests.Add(new(path, System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(path)), false, PreserveSourceLink: info.IsSymbolicLink));
                }
                if (directoryTargets.Count > 0 && book.Source.IsDirectory) physicalBook = await archives.GetPhysicalPathAsync(book.Path, preparationToken);
                preparationToken.ThrowIfCancellationRequested();
                if (_closing || generation != _generation || !ReferenceEquals(book, Book)) throw new OperationCanceledException();
                return requests;
            }, pending.Token, async (plan, validationToken) =>
            {
                await ProtectBookTransferDestinationAsync(plan.Target.Path, validationToken);
                await ProtectBookTransferDestinationAsync(plan.Destination, validationToken);
            });
            if (!_closing && generation == _generation) Error = _destinationMoves.Error ?? realized?.CapabilityWarning;
            bool changed = false;
            var affectedDirectories = new List<string>();
            if (physicalBook is not null)
                foreach (var result in results)
                {
                    // 系统realpath与传输记录的系统别名可能不同，按实际结果核对，不改写原定位。
                    var actual = await archives.GetPhysicalPathAsync(result.Destination, CancellationToken.None);
                    if (directoryTargets.Contains(actual) && IsInsideBook(actual, physicalBook))
                        affectedDirectories.Add(System.IO.Path.Combine(book.Path, System.IO.Path.GetRelativePath(physicalBook, actual)));
                }
            bool directoryChanged = affectedDirectories.Count > 0;
            foreach (var result in results)
            {
                // 归档或其提取输出不属于当前普通目录索引；只有真正来自当前目录的文件可补入。
                var page = pages.FirstOrDefault(page => page.ArchiveEntry.FilePath is { } path && System.IO.Path.GetFullPath(path) == result.Source);
                if (page is not null && !page.ArchiveEntry.TargetArchiveEntry.IsDirectory && !directoryChanged) changed |= await ApplyTransferredPageAsync(book, page, result, copy: true, readingAnchor);
            }
            if (directoryChanged)
            {
                // 目录覆盖可移除多个后代，不能伪造为单张图片；沿唯一打开链刷新实际索引。
                foreach (var page in book.Pages.SourcePages.Where(page => affectedDirectories.Any(target => IsInsideBook(page.ArchiveEntry.SystemPath, target)))) _fileImages?.InvalidatePage(page);
                refresh = book.CreateMemento(); refreshSearch = book.Pages.SearchKeyword; refreshPart = Position.Part;
            }
            if (changed) { await ProbeAroundAsync(book, Position.Index, CancellationToken.None); RebuildFrame(1); RecordPageHistory(); }
            if (results.Count > 0)
            {
                _fileImages?.InvalidateCovers();
                try { await saveData.SaveAsync(book, keepHistoryOrder: _keepHistoryOrder); }
                catch (Exception ex) { if (!_closing && generation == _generation) Error = "文件操作已成功，阅读状态保存失败，请重试保存：" + ex.Message; ScheduleSave(); }
            }
            failure = Error;
        }
        finally
        {
            lock (_fileCopySync) _fileCopyPreparation = null;
            try { if (realized is not null) await realized.DisposeAsync(); }
            catch (Exception ex) { if (!_closing && generation == _generation) failure = Error = "复制完成后临时实体清理失败：" + ex.Message; else System.Diagnostics.Trace.WriteLine(ex); }
            finally { _gate.Release(); Notify(); }
        }
        if (refresh is not null && !_closing && !_disposed && generation == _generation && ReferenceEquals(requestedBook, Book))
        {
            var expected = generation + 1;
            var restored = await OpenCoreAsync(refresh.Path, CancellationToken.None, keepHistoryOrder: true, startupMemento: refresh, pageSearchKeyword: refreshSearch);
            await _gate.WaitAsync();
            try
            {
                if (!_closing && !_disposed && expected == _generation)
                {
                    if (restored && Book?.CurrentPage?.EntryName == refresh.Page) { Position = new(Position.Index, refreshPart); RebuildFrame(MoveDirection); ScheduleSave(); }
                    if (failure is not null) Error = failure;
                    if (!restored) Error = "复制已完成，当前目录重载失败：" + Error;
                    Notify();
                }
            }
            finally { _gate.Release(); }
        }
    }
    /// <summary>保留文件系统大小写语义，按路径分隔边界判断目录覆盖影响范围。</summary>
    /// <param name="path">实际结果或页面地址。</param><param name="root">当前书籍或目标目录。</param><returns>自身或后代路径为true。</returns>
    private static bool IsInsideBook(string path, string root) => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
    /// <summary>单项结果沿原来源集合协调；批次目标已捕获，推进页面不重采集后续操作对象。</summary>
    private async Task<bool> ApplyTransferredPageAsync(Book book, Page page, FileTransferResult result, bool copy, Page? readingAnchor)
    {
        var relative = System.IO.Path.GetRelativePath(book.Path, result.Destination);
        bool withinSource = relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative)
            && (book.Setting.IsRecursiveFolder || !relative.Contains('/'));
        if (copy && !withinSource) return false;
        int previous = page.Index;
        var next = book.Pages.Skip(previous + 1).FirstOrDefault() ?? book.Pages.Take(previous).LastOrDefault();
        var source = book.Pages.SourcePages.Where(p => (copy || !ReferenceEquals(p, page)) && p.ArchiveEntry.FilePath != result.Destination).ToList();
        if (withinSource)
        {
            var moved = new Page(new ArchiveEntry(book.Source) { Id = book.Pages.SourcePages.Max(p => p.EntryIndex) + 1, RawEntryName = relative,
                FilePath = result.Destination, Length = page.ArchiveEntry.Length, LastWriteTime = page.ArchiveEntry.LastWriteTime }, archives: page.Content.Archives, folders: page.Content.FolderConfigs);
            moved.Content.PageDataSource = page.Content.PageDataSource; moved.Content.HasSize = page.Content.HasSize; source.Add(moved);
        }
        book.Pages.SetSourcePages(source); await book.SortAsync(CancellationToken.None); if (!copy) _fileSelection = null;
        if (book.Pages.Count == 0) book.CurrentPage = book.Pages.SourcePages.FirstOrDefault(p => p.EntryIndex > page.EntryIndex) ?? book.Pages.SourcePages.LastOrDefault();
        var anchor = copy ? readingAnchor : next;
        Position = new(anchor is not null && book.Pages.Contains(anchor) ? anchor.Index : 0, copy ? Position.Part : 0);
        return true;
    }
    /// <summary>共享历史允许跨书撤销；仍浏览恢复目录才重载定位，否则仅回报真实路径。</summary>
    public async Task ReplayDestinationMoveAsync(bool undo, CancellationToken token = default)
    {
        if (_destinationMoves is null || _destinationMoves.IsBusy || IsUsingClipboard || IsDeletingFile || !Config.Current.System.IsFileWriteAccessEnabled) return;
        FileTransferResult? result; Book? book; long generation; string? entry = null; bool reload = false;
        await _gate.WaitAsync(token);
        try
        {
            if (_disposed || _closing || IsLoading || IsUsingClipboard || IsRenamingBook) return;
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
                                book.Pages.SetSourcePages(book.Pages.SourcePages.Where(p => !ReferenceEquals(p, page))); await book.SortAsync(CancellationToken.None);
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
