using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原窗口布局宿主：只装配表现和系统交互，阅读规则在 Engine。</summary>
public sealed partial class MainWindow : Window
{
    private ReaderWorkspaceViewModel? _model;
    private BitmapFactory? _images;
    private IPlatformService? _platform;
    private IPlatformInput? _platformInput;
    private bool _closedPrepared;
    private bool _preparing;
    private Task? _shutdown;
    private CancellationTokenSource? _folders;
    private Book? _folderBook;
    private string? _bookmarkOpenTarget;
    private bool _restoredBookshelfHonored;
    private bool _sliderDragging;
    private bool _sliderUpdating;
    private readonly Dictionary<(object Scope, string Gesture), double> _wheelDeltas = [];
    private double _sliderWheel;
    private double _leftWidth, _rightWidth;
    private SidePanelPresenter? _sidePanels;
    private AutoHidePresenter? _autoHide;
    private WindowState _lastFullScreenState = WindowState.Normal;
    public ReaderView Viewer => this.FindControl<ReaderView>("MainViewSocket")!;
    private ThumbnailView FilmStrip => this.FindControl<ThumbnailView>("DockFilmStripSocket")!;
    private ThumbnailView NavigatorView => this.FindControl<ThumbnailView>("Navigator")!;
    private SliderTextBox PageNumber => this.FindControl<SliderTextBox>("PageNumberView")!;
    private static readonly HashSet<string> HostCommands = new(StringComparer.Ordinal)
    {
        "LoadAs", "OpenFolder", "ReLoad", "ParentFolder", "OpenExplorer", "CloseWindow", "CloseApplication", "ToggleFullScreen", "MoveToFolderAs", "CopyToFolderAs",
        "ViewScaleUp", "ViewScaleDown", "ViewScrollUp", "ViewScrollDown", "ViewScrollLeft", "ViewScrollRight", "OpenContextMenu", "SetStretchModeUniform", "SetStretchModeNone", "ToggleHideLeftPanel", "ToggleHideRightPanel",
        "ViewBaseScaleUp", "ViewBaseScaleDown", "ViewRotateLeft", "ViewRotateRight", "ToggleBookLock", "Unload", "ToggleViewFlipHorizontal", "ViewFlipHorizontalOn", "ViewFlipHorizontalOff",
        "ToggleViewFlipVertical", "ViewFlipVerticalOn", "ViewFlipVerticalOff", "ViewReset", "ViewScaleStretch", "ViewPresetScroll", "ViewScrollNTypeUp", "ViewScrollNTypeDown",
        "ToggleStretchMode", "ToggleStretchModeReverse", "SetStretchModeUniformToFill", "SetStretchModeUniformToSize", "SetStretchModeUniformToVertical", "SetStretchModeUniformToHorizontal", "ToggleStretchAllowScaleUp", "ToggleStretchAllowScaleDown", "ToggleHoverScroll",
        "OpenOptionsWindow", "HelpCommandList", "ToggleBookmark", "LoadRecentBook", "OpenBookExplorer",
        "ToggleVisibleBookshelf", "ToggleVisiblePageList", "ToggleVisibleHistoryList", "ToggleVisibleFileInfo", "ToggleVisibleBookmarkList", "ToggleVisibleNavigator",
        "ToggleVisibleFilmStrip", "ToggleHideFilmStrip", "ToggleVisiblePlaylist", "NextScrollPage", "PrevScrollPage", "JumpPage", "NextSizePage", "PrevSizePage",
        "EnterBookshelfFolder", "SyncBookshelfFolder", "RefreshBookshelfFolder", "ToggleVisibleFoldersTree", "ToggleVisibleContentsTree", "FocusMainView", "FocusFolderSearchBox", "FocusPageListSearchBox", "RegisterBookmark", "FocusHistorySearchBox", "FocusBookmarkList", "FocusBookmarkSearchBox", "ClearHistory", "ClearHistoryInPlace", "RemoveUnlinkedHistory", "ToggleHideMenu", "ToggleHidePanel", "ToggleHidePageSlider", "ToggleVisibleSideBar", "ShowHiddenPanels", "SetFullScreen", "CancelFullScreen", "ToggleTopmost"
    };
    // 浏览增量仅支持整体缩放/平移；分页特有旋转/翻转/适配明确禁用，不能用空执行冒充支持。
    private static readonly HashSet<string> PagedTransformCommands = new(StringComparer.Ordinal)
    {
        "ViewBaseScaleUp", "ViewBaseScaleDown", "ViewRotateLeft", "ViewRotateRight", "ViewReset",
        "ToggleViewFlipHorizontal", "ViewFlipHorizontalOn", "ViewFlipHorizontalOff", "ToggleViewFlipVertical", "ViewFlipVerticalOn", "ViewFlipVerticalOff",
        "SetStretchModeUniform", "SetStretchModeNone", "ToggleStretchMode", "ToggleStretchModeReverse", "SetStretchModeUniformToFill",
        "SetStretchModeUniformToSize", "SetStretchModeUniformToVertical", "SetStretchModeUniformToHorizontal", "ToggleStretchAllowScaleUp", "ToggleStretchAllowScaleDown", "ToggleHoverScroll"
    };
    /// <summary>由启动层接入原生事件；只消费主查看器区域，其余控件使用框架输入。</summary>
    public void AttachPlatformInput(IPlatformInput input)
    {
        _platformInput?.Dispose(); _platformInput = input; input.Attach(HandlePlatformGesture);
    }
    /// <summary>真实精确滚动连续平移，捏合围绕当前指针缩放，不使用增量大小猜设备。</summary>
    public bool HandlePlatformGesture(PlatformGesture gesture)
    {
        if (_preparing || !IsActive || WindowInteraction.HasDialog(this) || _model?.Operation.Book is null) return false;
        if (gesture.SourceWindow != (TryGetPlatformHandle()?.Handle ?? 0)) return false;
        var point = Viewer.TranslatePoint(new Point(0, 0), this);
        if (point is null) return false;
        var local = new Point(gesture.X - point.Value.X, gesture.Y - point.Value.Y);
        if (!new Avalonia.Rect(Viewer.Bounds.Size).Contains(local)) return false;
        // 自动隐藏栏覆盖同一查看器范围；按实际命中控件排除菜单/侧栏/底栏，不能只比较矩形。
        if (this.InputHitTest(new Point(gesture.X, gesture.Y)) is not Visual hit ||
            (!ReferenceEquals(hit, Viewer) && !hit.GetVisualAncestors().Contains(Viewer))) return false;
        _autoHide?.LeaveVisibleLocked();
        if (gesture.IsMagnify) _ = ZoomFromGestureAsync(1 + gesture.Magnification, local);
        else Viewer.Pan(new(gesture.DeltaX, gesture.DeltaY));
        return true;
    }
    /// <summary>观察异步缩放错误，原生回调不可阻塞等待解码。</summary>
    private async Task ZoomFromGestureAsync(double factor, Point point)
    {
        try { if (factor > 0) await Viewer.ZoomAsync(factor, point); } catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>加载可独立验收的布局，设计器和 Headless 不需要具体后端。</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, Key_Down, RoutingStrategies.Tunnel);
        // Finder复制不触发阅读回报，菜单展开/窗口重新激活时只重查命令能力。
        this.FindControl<Menu>("MenuBar")!.AddHandler(MenuItem.SubmenuOpenedEvent, (_, _) => RefreshHistoryCommandStates());
        Activated += (_, _) => RefreshHistoryCommandStates();
        AddHandler(DragDrop.DropEvent, Drop);
        DragDrop.SetAllowDrop(this, true);
        Viewer.TryGestureRequested = TryHandleGesture;
        Viewer.CanStartMouseSequence = () => !_preparing && !_closedPrepared && _model?.Operation.IsLoading == false && !WindowInteraction.HasDialog(this) && !this.FindControl<Menu>("MenuBar")!.IsOpen && Viewer.ContextMenu?.IsOpen != true;
        Viewer.MouseSequenceText = sequence => FindMouseSequence(sequence)?.Text;
        Viewer.TryMouseSequenceRequested = sequence => { var command = FindMouseSequence(sequence); if (command is null) return false; _ = ExecuteInputAsync(command.Name, true); return true; };
        Viewer.ChildBookRequested += async (_, page) =>
        {
            if (_model is not null && !_preparing && !_closedPrepared)
                try { await _model.Operation.OpenChildBookAsync(page, _model.Operation.Book); } catch (Exception ex) { ShowError(ex.Message); }
        };
        Viewer.PointerWheelChanged += Viewer_Wheel;
        // 滑条内部 Thumb 会处理指针事件；隧道路由保证释放确认不被模板吞掉。
        var slider = this.FindControl<Slider>("PageSliderView")!;
        slider.AddHandler(PointerPressedEvent, Slider_Pressed, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, Slider_Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(PointerWheelChangedEvent, Slider_Wheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageNumber.ReturnFocusRequested += (_, _) => Viewer.Focus();
        PageNumber.NavigationFailed += (_, ex) => ShowError(ex.Message);
        AttachBookmarkInput();
        // 历史列表模板会消费按下事件；隧道记录命中行，释放按原单/双击配置打开。
        var history = this.FindControl<ListBox>("HistoryList")!;
        history.AddHandler(PointerPressedEvent, History_Pressed, RoutingStrategies.Tunnel);
        history.AddHandler(PointerReleasedEvent, History_Released, RoutingStrategies.Bubble, handledEventsToo: true);
        this.FindControl<TextBox>("HistorySearchBox")!.AddHandler(KeyDownEvent, HistorySearch_KeyDown, RoutingStrategies.Tunnel);
        Closing += Window_Closing;
        Opened += async (_, _) => await RunStartupHistoryCleanupAsync();
    }
    /// <summary>由唯一启动层传入已经装配的契约，不在控件中创建解码或存储实现。</summary>
    public void Bind(ReaderWorkspaceViewModel model, BitmapFactory images, IPlatformService platform)
    {
        _model = model; _images = images; _platform = platform; DataContext = model;
        model.Operation.PageEndDialogAsync = ShowPageEndDialogAsync;
        // 编辑树保留宿主表现绑定；独立列表的 DataContext 可以独立更换。
        this.FindControl<TreeView>("BookmarkTree")!.DataContext = model;
        var bookmarks = this.FindControl<BookmarkListView>("BookmarkPanelList")!;
        bookmarks.OpenBookAsync = async node =>
        {
            if (_preparing || _closedPrepared || node.Path is not { } path) return;
            if (!Config.Current.Bookmark.IsSyncBookshelfEnabled) _bookmarkOpenTarget = path;
            await OpenAsync(path);
        };
        bookmarks.CurrentBookPath = () => model.Operation.Book?.Path;
        bookmarks.ReadMetadataAsync = model.Operation.GetFileMetadataAsync;
        bookmarks.SaveSettingsAsync = () => model.Operation.SaveConfigurationAsync();
        bookmarks.Failed += (_, message) => ShowError(message);
        bookmarks.TreeVisibilityUpdated += (_, _) => model.RefreshBookmarkTree();
        bookmarks.SelectionUpdated += (_, _) =>
        {
            if (_preparing || _closedPrepared) return;
            if (_bookmarkTreeSelection && this.FindControl<TreeView>("BookmarkTree")!.IsKeyboardFocusWithin) return;
            _bookmarkTreeSelection = false;
            model.SelectedBookmark = bookmarks.SelectedNodes.FirstOrDefault(); model.BookmarkSelectionCount = bookmarks.SelectedNodes.Count;
        };
        bookmarks.Attach(model.SaveData);
        AttachListTemplates();
        AttachDirectoryTree();
        AttachDestinationFolders();
        var playlist = this.FindControl<PlaylistView>("PlaylistPanelView")!;
        playlist.Failed += (_, message) => ShowError(message); playlist.Attach(model.Operation);
        model.Operation.MarkersChanged += Model_MarkersChanged;
        model.HistoryRefreshed += History_Refreshed;
        Viewer.Attach(model.Operation, images); model.Refreshed += Model_Refreshed;
        model.PanelsRefreshed += Model_PanelsRefreshed;
        _leftWidth = model.LeftWidth; _rightWidth = model.RightWidth; UpdatePanelColumns(); model.Attach();
        FilmStrip.Attach(model.Operation, images); NavigatorView.Attach(model.Operation, images);
        _sidePanels = new(this, model);
        _sidePanels.FloatingKeyDown += Key_Down;
        FilmStrip.PageRequested += async (_, index) => { try { await model.Operation.JumpAsync(index); } catch (Exception ex) { ShowError(ex.Message); } };
        FilmStrip.FrameMoveRequested += async (_, direction) => { try { await model.Operation.MoveAsync(direction); } catch (Exception ex) { ShowError(ex.Message); } };
        FilmStrip.GlobalWheelRequested += FilmStrip_GlobalWheel;
        NavigatorView.NavigateRequested += (_, point) => Viewer.Navigate(point);
        PageNumber.RequestPageAsync = async (source, index) =>
        {
            if (_preparing || _closedPrepared || !ReferenceEquals(source, model.Operation.Book)) return;
            // 原 SelectedIndexRaw 绕过滑块的双页对齐；互斥内再次核对来源，避免旧输入跳新书。
            await model.Operation.JumpAsync(index, expectedBook: source as Book);
            if (!_preparing && !_closedPrepared) model.RefreshSelection();
        };
        _autoHide = new(this, model);
        model.ChromeRefreshed += Model_ChromeRefreshed;
        UpdatePanelColumns(); BuildMenus();
    }
    /// <summary>返回真实执行能力，菜单占位与输入状态使用同一判断。</summary>
    public bool IsCommandAvailable(string name) => name switch
    {
        var command when PagedTransformCommands.Contains(command) && _model?.Operation.IsFrameReading != true => false,
        "Unload" => _model?.Operation.CanUnload == true,
        "MoveToFolderAs" or "CopyToFolderAs" or "MoveBookToFolderAs" or "CopyBookToFolderAs" => IsDestinationCommandAvailable(name),
        "DeleteFile" => _model?.Operation.CanDeleteFile == true,
        "DeleteBook" => _model?.Operation.CanDeleteBook == true,
        "RenameBook" => _model?.Operation.CanRenameBook == true,
        "CopyFile" => _model?.Operation is { } copy && copy.CanCopyFiles(copy.GetCopyFileParameter().MultiPagePolicy),
        "CopyBook" => _model?.Operation.CanCopyBook == true,
        "Paste" => _model?.Operation.CanPasteFiles == true,
        "CutFile" or "CutBook" => false,
        "SetSortModeEntry" or "SetSortModeEntryDescending" => _model?.Operation.IsLoading == false && _model.Operation.Book?.Source.IsPlaylist == true,
        "UndoDestinationMove" => _model?.Operation is { IsDeletingFile: false, IsRenamingBook: false, IsUsingClipboard: false, IsTransferringBook: false } && Config.Current.System.IsFileWriteAccessEnabled && _model.Operation.DestinationMoves?.CanUndo == true,
        "RedoDestinationMove" => _model?.Operation is { IsDeletingFile: false, IsRenamingBook: false, IsUsingClipboard: false, IsTransferringBook: false } && Config.Current.System.IsFileWriteAccessEnabled && _model.Operation.DestinationMoves?.CanRedo == true,
        var command when command.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal) => IsDestinationCommandAvailable(command),
        "MoveToParentBook" => _model?.Operation.CanMoveToParentBook == true,
        "MoveToChildBook" => _model?.Operation.CanMoveToChildBook == true,
        "ToggleIsRecursiveFolder" => _model?.Operation.Book is not null && !_model.Operation.IsLoading,
        _ => IsCommandImplemented(name)
    };
    /// <summary>配置/占位说明读取迁移状态，不能把当前无目标误标成尚未迁移。</summary>
    private bool IsCommandImplemented(string name) => name == "PreviewProfileImport" ? _profileImport is not null : name == "ImportBackup" ? _applyProfileImport is not null : HostCommands.Contains(name) || _model?.Commands.IsAvailable(name) == true;
    /// <summary>迁入完整菜单后追加 Mac 打开目录及已有交互，保持原八组顺序。</summary>
    private void BuildMenus()
    {
        if (_model is null) return;
        var root = MenuTree.CreateDefault();
        root.Children![0].Children!.Insert(1, new("打开目录…", MenuElementType.Command, "OpenFolder"));
        root.Children[0].Children!.Add(new("重新载入", MenuElementType.Command, "ReLoad"));
        root.Children[0].Children!.Add(new("关闭窗口", MenuElementType.Command, "CloseWindow"));
        root.Children[0].Children!.Add(new("旧数据导入预览…", MenuElementType.Command, "PreviewProfileImport"));
        foreach (var (text, command) in new[] { ("放大", "ViewScaleUp"), ("缩小", "ViewScaleDown") })
            root.Children[2].Children!.Add(new(text, MenuElementType.Command, command));
        // 原默认菜单未列出的定位命令追加到跳转组，原节点/占位和顺序完整保留。
        root.Children[3].Children!.Add(new(null, MenuElementType.Separator, null));
        foreach (var name in new[] { "JumpPage", "PrevHistoryPage", "NextHistoryPage", "PrevBookHistory", "NextBookHistory" })
            root.Children[3].Children!.Add(new(null, MenuElementType.Command, name));
        MenuPresenter.Populate(this.FindControl<Menu>("MenuBar")!, root, _model.Commands, _model.SaveData, IsCommandImplemented, name => ExecuteAsync(name, true), GetCommandCheck);
        RefreshHistoryCommandStates();
    }
    /// <summary>原菜单绑定的勾选表现；只读取引擎配置，不在菜单中维护第二套状态。</summary>
    private bool? GetCommandCheck(string name) => name switch
    {
        "ToggleBookLock" => _model?.Operation.IsBookLocked,
        "ToggleIsPanorama" => Config.Current.Book.IsPanorama,
        "SetPageOrientationHorizontal" => Config.Current.Book.Orientation == PageFrameOrientation.Horizontal,
        "SetPageOrientationVertical" => Config.Current.Book.Orientation == PageFrameOrientation.Vertical,
        var command when CommandTable.BookOrderCommands.TryGetValue(command, out var order) => _model?.Operation.Bookshelf.FolderOrder == order,
        "SetPageModeOne" => _model?.Operation.Book?.Setting.PageMode == PageMode.SinglePage,
        "SetPageModeTwo" => _model?.Operation.Book?.Setting.PageMode == PageMode.WidePage,
        "SetBookReadOrderRight" => _model?.Operation.Book?.Setting.BookReadOrder == PageReadOrder.RightToLeft,
        "SetBookReadOrderLeft" => _model?.Operation.Book?.Setting.BookReadOrder == PageReadOrder.LeftToRight,
        "ToggleIsSupportedDividePage" => _model?.Divide,
        "ToggleIsSupportedWidePage" => _model?.Wide,
        "ToggleIsSupportedSingleFirstPage" => _model?.FirstSingle,
        "ToggleIsSupportedSingleLastPage" => _model?.LastSingle,
        "ToggleIsRecursiveFolder" => _model?.Operation.Book?.Setting.IsRecursiveFolder == true,
        "ToggleBookmark" => _model?.IsBookmark,
        "TogglePlaylistItem" => _model?.IsPlaylistMarked,
        "ToggleVisiblePlaylist" => _model?.ShowPlaylist,
        "ToggleVisibleBookshelf" => _model?.ShowFolderList,
        "ToggleVisibleFoldersTree" => Config.Current.Bookshelf.IsFolderTreeVisible && _model?.ShowFolderList == true,
        "ToggleVisibleContentsTree" => Config.Current.PageList.IsFolderTreeVisible && _model?.ShowPageList == true,
        "ToggleVisiblePageList" => _model?.ShowPageList,
        "ToggleVisibleHistoryList" => _model?.ShowHistory,
        "ToggleVisibleFileInfo" => _model?.ShowInformation,
        "ToggleVisibleBookmarkList" => _model?.ShowBookmarks,
        "ToggleVisibleNavigator" => _model?.ShowNavigator,
        "ToggleVisibleFilmStrip" => Config.Current.FilmStrip.IsEnabled,
        "ToggleHideFilmStrip" => Config.Current.FilmStrip.IsHideFilmStrip,
        "ToggleHideMenu" => Config.Current.MenuBar.IsHideMenu,
        "ToggleHidePanel" => Config.Current.Panels.IsHideLeftPanel || Config.Current.Panels.IsHideRightPanel,
        "ToggleHideLeftPanel" => Config.Current.Panels.IsHideLeftPanel,
        "ToggleHideRightPanel" => Config.Current.Panels.IsHideRightPanel,
        "ToggleHidePageSlider" => Config.Current.Slider.IsHidePageSlider,
        "ToggleVisibleSideBar" => Config.Current.Panels.IsSideBarEnabled,
        "ToggleFullScreen" => WindowState == WindowState.FullScreen,
        "ToggleTopmost" => Config.Current.Window.IsTopmost,
        "ShowHiddenPanels" => _autoHide?.IsVisibleLocked,
        "SetStretchModeUniform" => Config.Current.View.StretchMode == PageStretchMode.Uniform,
        "SetStretchModeNone" => Config.Current.View.StretchMode == PageStretchMode.None,
        "SetStretchModeUniformToFill" => Config.Current.View.StretchMode == PageStretchMode.UniformToFill,
        "SetStretchModeUniformToSize" => Config.Current.View.StretchMode == PageStretchMode.UniformToSize,
        "SetStretchModeUniformToVertical" => Config.Current.View.StretchMode == PageStretchMode.UniformToVertical,
        "SetStretchModeUniformToHorizontal" => Config.Current.View.StretchMode == PageStretchMode.UniformToHorizontal,
        "ToggleHoverScroll" => Config.Current.Mouse.IsHoverScroll,
        "ToggleStretchAllowScaleUp" => Config.Current.View.AllowStretchScaleUp,
        "ToggleStretchAllowScaleDown" => Config.Current.View.AllowStretchScaleDown,
        "ToggleViewFlipHorizontal" => Viewer.IsFlipHorizontal,
        "ToggleViewFlipVertical" => Viewer.IsFlipVertical,
        _ => null
    };
    /// <summary>表现变化只更新列宽；GridSplitter 的实际宽度由窗口保存，控件不设置固定 Width。</summary>
    private void Model_PanelsRefreshed(object? sender, EventArgs e)
    {
        if (_model is not null) _model.Operation.Bookshelf.IsPresented = _model.ShowFolderList;
        _autoHide?.Refresh(); UpdatePanelColumns();
        _sidePanels?.Refresh();
        _ = NavigatorView.RefreshAsync();
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
    }
    /// <summary>仅更新覆盖区域，不重建侧栏或重新申请正文图像。</summary>
    private void Model_ChromeRefreshed(object? sender, EventArgs e) => UpdatePanelColumns();
    /// <summary>原自动隐藏栏覆盖正文；普通停靠栏占据列，隐藏弹出不改变查看器尺寸。</summary>
    private void UpdatePanelColumns()
    {
        if (_model is null) return;
        var columns = this.FindControl<Grid>("SidePanelFrame")!.ColumnDefinitions;
        if (columns[1].ActualWidth > 0) _leftWidth = columns[1].ActualWidth;
        if (columns[5].ActualWidth > 0) _rightWidth = columns[5].ActualWidth;
        columns[0].Width = new GridLength(_model.SideBarVisible ? 41 : 0);
        columns[6].Width = new GridLength(_model.SideBarVisible ? 41 : 0);
        columns[1].Width = new GridLength(_model.LeftVisible || _model.LeftAutoHide ? Math.Max(100, _leftWidth) : 0);
        columns[5].Width = new GridLength(_model.RightVisible || _model.RightAutoHide ? Math.Max(100, _rightWidth) : 0);
        columns[2].Width = new GridLength(_model.LeftVisible ? 4 : 0);
        columns[4].Width = new GridLength(_model.RightVisible ? 4 : 0);
        int start = _model.LeftAutoHide ? 0 : 3;
        int end = _model.RightAutoHide ? 7 : 4;
        Grid.SetColumn(Viewer, start); Grid.SetColumnSpan(Viewer, end - start);
    }
    /// <summary>打开请求统一进入 BookOperation，窗口不枚举内容。</summary>
    public async Task OpenAsync(string path)
    {
        if (_model is null) return; await _model.Operation.OpenAsync(path);
    }
    /// <summary>Finder/拖入多路径转交原加载链，视图不生成或解析临时列表。</summary>
    /// <param name="paths">系统提供的本机地址顺序。</param><returns>业务加载完成任务。</returns>
    public Task OpenFilesAsync(IEnumerable<string> paths) => _model is null || _preparing || _closedPrepared ? Task.CompletedTask : _model.Operation.OpenFilesAsync(paths);
    /// <summary>启动和无窗口重开传入原完整快照，页面恢复不依赖已删除的历史项。</summary>
    public async Task RestoreLastAsync()
    {
        if (_model is null) return;
        await _model.Operation.RestoreLastAsync();
    }
    /// <summary>菜单与历史面板共用原设置窗口；关闭后只刷新表现，不另建阅读入口。</summary>
    /// <param name="history">是否定位到历史记录设置页面。</param>
    private async Task ShowOptionsAsync(bool history = false)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var settings = new SettingsWindow(_model, IsCommandImplemented, _platform);
        var pageFormat = Config.Current.PageList.Format; var recursiveSearch = Config.Current.Bookshelf.IsSearchIncludeSubdirectories;
        if (history) settings.SelectHistoryPage();
        await settings.ShowDialog(this);
        if (settings.WasSaved && !_preparing && !_closedPrepared) RefreshFonts();
        if (settings.WasSaved && _themePresenter is not null) await _themePresenter.RefreshAsync();
        if (_preparing || _closedPrepared) return;
        _model.RefreshSelection(); _model.RefreshPanels(); await FilmStrip.RefreshAsync(); BuildMenus();
        _model.RefreshNavigationPanel(); RefreshPageTreeLayout(); RefreshFolderTreeLayout(); _pagePresentation?.RefreshCovers();
        if (pageFormat != Config.Current.PageList.Format && _model.Operation.Book is { } book && book.Pages.SearchKeyword.Length > 0) await _model.Operation.SearchPagesAsync(book.Pages.SearchKeyword, book);
        if (recursiveSearch != Config.Current.Bookshelf.IsSearchIncludeSubdirectories && _model.Operation.Bookshelf.SearchKeyword.Length > 0) await _model.Operation.Bookshelf.RefreshAsync();
        await Viewer.RefreshAsync();
    }

    /// <summary>执行宿主命令或转交原阅读命令；错误显示给用户。</summary>
    public async Task ExecuteAsync(string name, bool fromMenu = false)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        if (PagedTransformCommands.Contains(name) && !_model.Operation.IsFrameReading)
        { ShowError("当前展示方式暂不支持此变换，切回分页可使用。"); return; }
        try
        {
            switch (name)
            {
                case "ImportBackup":
                case "PreviewProfileImport": await ShowProfileImportAsync(); break;
                case "LoadAs":
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "打开图片、ZIP / RAR / 7z", AllowMultiple = false });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } file) await OpenAsync(file); break;
                case "OpenFolder":
                    var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "打开图片目录", AllowMultiple = false });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) await OpenAsync(folder); break;
                case "ReLoad": if (_model.Operation.Book is { } reload) await OpenAsync(reload.Path); break;
                case "ParentFolder":
                    await _model.Operation.Bookshelf.UpAsync(); break;
                case "EnterBookshelfFolder": await _model.Operation.Bookshelf.EnterAsync(); break;
                case "SyncBookshelfFolder":
                    if (_model.Operation.Book is { } current) await _model.Operation.Bookshelf.SyncAsync(current, force: true);
                    if (Config.Current.Bookshelf.IsSyncFolderTree && Config.Current.Bookshelf.IsFolderTreeVisible) await _model.Operation.Bookshelf.FolderTree.SyncDirectoryAsync(_model.Operation.Bookshelf.Place, true); break;
                case "RefreshBookshelfFolder":
                    RefreshListCovers();
                    if (Config.Current.Bookshelf.IsFolderTreeVisible) await _model.Operation.Bookshelf.FolderTree.RefreshDirectoryAsync();
                    if (await _model.Operation.Bookshelf.RefreshAsync()) await _model.Operation.SaveAsync(); break;
                case "ToggleVisibleFoldersTree":
                    bool treeVisible = !_model.ShowFolderList || !Config.Current.Bookshelf.IsFolderTreeVisible;
                    _model.ShowPanel("FolderPanel"); await SetFolderTreeVisibleAsync(treeVisible); break;
                case "OpenExplorer":
                    if (_model.Operation.Book is { } book) await _platform!.RevealAsync(book.CurrentPage?.ArchiveEntry.FilePath ?? book.Path); break;
                case "CloseWindow": Close(); break;
                case "CloseApplication": (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown(); break;
                case "ToggleFullScreen": SetFullScreen(WindowState != WindowState.FullScreen); break;
                case "SetFullScreen": SetFullScreen(true); break;
                case "CancelFullScreen": SetFullScreen(false); break;
                case "ToggleTopmost": Config.Current.Window.IsTopmost = !Config.Current.Window.IsTopmost; _autoHide?.Refresh(); break;
                case "ShowHiddenPanels": _autoHide?.ShowHiddenPanels(); break;
                case "ToggleHideMenu": Config.Current.MenuBar.IsHideMenu = !Config.Current.MenuBar.IsHideMenu; _model.RefreshPanels(); break;
                case "ToggleHidePageSlider": Config.Current.Slider.IsHidePageSlider = !Config.Current.Slider.IsHidePageSlider; _model.RefreshPanels(); break;
                case "ToggleVisibleSideBar": Config.Current.Panels.IsSideBarEnabled = !Config.Current.Panels.IsSideBarEnabled; _model.RefreshPanels(); break;
                case "ToggleHidePanel":
                    bool hide = !(Config.Current.Panels.IsHideLeftPanel || Config.Current.Panels.IsHideRightPanel);
                    Config.Current.Panels.IsHideLeftPanel = hide; Config.Current.Panels.IsHideRightPanel = hide; _model.RefreshPanels(); break;
                case "ToggleBookLock":
                    _model.Operation.SetBookLock(_model.SaveData.GetCommandParameter<ToggleCommandParameter>(name).GetState(_model.Operation.IsBookLocked, fromMenu)); break;
                case "Unload": await _model.Operation.UnloadAsync(); _model.Address = _model.Operation.Book?.Path ?? ""; break;
                case "ViewScaleUp": case "ViewScaleDown": case "ViewBaseScaleUp": case "ViewBaseScaleDown":
                    await Viewer.ScaleAsync(name.EndsWith("Up") ? 1 : -1, _model.SaveData.GetCommandParameter<ViewScaleCommandParameter>(name), name.StartsWith("ViewBase")); break;
                case "ViewRotateLeft": case "ViewRotateRight":
                    await Viewer.RotateAsync(name.EndsWith("Right") ? 1 : -1, _model.SaveData.GetCommandParameter<ViewRotateCommandParameter>(name)); break;
                case "ToggleViewFlipHorizontal": case "ToggleViewFlipVertical":
                    bool horizontal = name.EndsWith("Horizontal");
                    Viewer.Flip(horizontal, _model.SaveData.GetCommandParameter<ToggleCommandParameter>(name).GetState(horizontal ? Viewer.IsFlipHorizontal : Viewer.IsFlipVertical, fromMenu)); break;
                case "ViewFlipHorizontalOn": case "ViewFlipHorizontalOff": case "ViewFlipVerticalOn": case "ViewFlipVerticalOff":
                    Viewer.Flip(name.Contains("Horizontal"), name.EndsWith("On")); break;
                case "ToggleHoverScroll": Config.Current.Mouse.IsHoverScroll = _model.SaveData.GetCommandParameter<ToggleCommandParameter>(name).GetState(Config.Current.Mouse.IsHoverScroll, fromMenu); await Viewer.RefreshAsync(); break;
                case "ViewReset": Viewer.ResetTransform(); await Viewer.RefreshAsync(); break;
                case "ViewScaleStretch": await Viewer.StretchAsync(); break;
                case "ViewPresetScroll": Viewer.ScrollToPreset(_model.SaveData.GetCommandParameter<ViewPresetScrollCommandParameter>(name)); break;
                case "ViewScrollNTypeUp": case "ViewScrollNTypeDown": Viewer.ScrollNType(name.EndsWith("Down") ? 1 : -1, _model.SaveData.GetCommandParameter<ViewScrollNTypeCommandParameter>(name)); break;
                case "ViewScrollUp": case "ViewScrollDown": case "ViewScrollLeft": case "ViewScrollRight":
                    Viewer.ScrollView(name, _model.SaveData.GetCommandParameter<ViewScrollCommandParameter>(name)); break;
                case "OpenContextMenu": OpenViewerContextMenu(); break;
                case "MoveToFolderAs": case "CopyToFolderAs": case "MoveBookToFolderAs": case "CopyBookToFolderAs": await OpenDestinationMoveMenuAsync(name); break;
                case "DeleteFile": await RunDestinationActionAsync(() => _model.Operation.DeleteFileAsync()); break;
                case "DeleteBook": await RunDestinationActionAsync(() => _model.Operation.DeleteBookAsync()); break;
                case "RenameBook": await RunDestinationActionAsync(() => _model.Operation.RenameBookAsync()); break;
                case "CopyFile": case "CopyBook": case "Paste":
                    await RunDestinationActionAsync(() => _model.Commands.ExecuteAsync(name)); break;
                case var command when command.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal):
                    await OpenDestinationMoveMenuAsync(command); break;
                case "UndoDestinationMove": case "RedoDestinationMove":
                    await RunDestinationActionAsync(() => _model.Operation.ReplayDestinationMoveAsync(name == "UndoDestinationMove")); break;
                case "NextScrollPage": await Viewer.ScrollToNextFrameAsync(1, _model.SaveData.GetScrollParameter(name)); break;
                case "PrevScrollPage": await Viewer.ScrollToNextFrameAsync(-1, _model.SaveData.GetScrollParameter(name)); break;
                case "NextSizePage": await _model.Operation.MoveSizeAsync(_model.SaveData.GetMoveSizeParameter().Size); break;
                case "PrevSizePage": await _model.Operation.MoveSizeAsync(-_model.SaveData.GetMoveSizeParameter().Size); break;
                case "JumpPage":
                    if (_model.Operation.Book?.Pages.Count > 0)
                    {
                        var number = await AskPageNumberAsync();
                        if (number is not null) await JumpPageAsync(number.Value);
                    }
                    break;
                case "ToggleStretchMode": case "ToggleStretchModeReverse":
                    Config.Current.View.StretchMode = NeeView.PageFrames.ViewTransformMath.ToggleStretch(Config.Current.View.StretchMode, name.EndsWith("Reverse") ? -1 : 1,
                        _model.SaveData.GetCommandParameter<ToggleStretchModeCommandParameter>(name)); await Viewer.RefreshAsync(); await Viewer.StretchAsync(); break;
                case "SetStretchModeNone": case "SetStretchModeUniform": case "SetStretchModeUniformToFill": case "SetStretchModeUniformToSize": case "SetStretchModeUniformToVertical": case "SetStretchModeUniformToHorizontal":
                    var mode = Enum.Parse<PageStretchMode>(name["SetStretchMode".Length..]);
                    var toggle = name != "SetStretchModeNone" && _model.SaveData.GetCommandParameter<StretchModeCommandParameter>(name).IsToggle;
                    Config.Current.View.StretchMode = toggle && Config.Current.View.StretchMode == mode ? PageStretchMode.None : mode;
                    await Viewer.RefreshAsync(); await Viewer.StretchAsync(); break;
                case "ToggleStretchAllowScaleUp": Config.Current.View.AllowStretchScaleUp = !Config.Current.View.AllowStretchScaleUp; await Viewer.RefreshAsync(); await Viewer.StretchAsync(); break;
                case "ToggleStretchAllowScaleDown": Config.Current.View.AllowStretchScaleDown = !Config.Current.View.AllowStretchScaleDown; await Viewer.RefreshAsync(); await Viewer.StretchAsync(); break;
                case "ToggleHideLeftPanel": Config.Current.Panels.IsHideLeftPanel = !Config.Current.Panels.IsHideLeftPanel; _model.RefreshPanels(); break;
                case "ToggleHideRightPanel": Config.Current.Panels.IsHideRightPanel = !Config.Current.Panels.IsHideRightPanel; _model.RefreshPanels(); break;
                case "OpenOptionsWindow":
                    await ShowOptionsAsync(); break;
                case "HelpCommandList": await ShowCommandStatusAsync(); break;
                case "ToggleBookmark":
                    if (_model.Operation.Book is { } marked) { await _model.Operation.SaveAsync(); await _model.SaveData.ToggleBookmarkAsync(marked); } break;
                case "RegisterBookmark":
                    if (_model.Operation.Book is { } register)
                    {
                        if (_preparing || _closedPrepared) break;
                        var selected = _model.SelectedBookmark;
                        var parent = selected?.IsFolder == true ? selected : selected is not null ? _model.SaveData.Bookmarks.ParentOf(selected) : null;
                        parent ??= this.FindControl<BookmarkListView>("BookmarkPanelList")!.Navigation?.Place ?? _model.SaveData.BookmarkRoot;
                        var edit = new BookmarkPopupEdit(register.CreateMemento(), parent, selected);
                        var choice = await new BookmarkRegistrationWindow(edit, _model.SaveData.BookmarkRoot, parent).ShowDialog<BookmarkRegistrationChoice?>(this);
                        if (choice is not null && !_preparing && !_closedPrepared)
                        {
                            // 保存回滚会重建树容器；失败仍选中原节点，方便修正后重试。
                            try { SelectBookmark(await _model.SaveData.ApplyBookmarkEditAsync(edit, choice.Parent, choice.Result)); }
                            catch { if (selected is not null && _model.SaveData.BookmarkRoot.Walk().Contains(selected)) SelectBookmark(selected); throw; }
                        }
                    }
                    break;
                case "LoadRecentBook":
                    var recent = _model.SaveData.HistoryEntries.FirstOrDefault(e => e.Path != _model.Operation.Book?.Path);
                    if (recent is not null) await OpenAsync(recent.Path); break;
                case "RemoveUnlinkedHistory": await CleanupHistoryAsync(); break;
                case "ClearHistoryInPlace":
                    if (await ConfirmAsync("清理当前位置历史", "移除当前书架列表真实目标的历史记录？不会删除文件或书签。", "移除") && !_preparing && !_closedPrepared)
                        await _model.Operation.ClearHistoryInPlaceAsync(); break;
                case "ClearHistory": if (!_model.Operation.IsLoading) await _model.SaveData.ClearHistoryAsync(); break;
                case "FocusHistorySearchBox":
                    Config.Current.History.IsVisibleSearchBox = true; _model.RefreshHistory(); _model.ShowPanel("HistoryPanel");
                    UpdateLayout(); var historySearch = this.FindControl<TextBox>("HistorySearchBox")!; historySearch.Focus(); historySearch.SelectAll(); break;
                case "FocusMainView": Viewer.Focus(); break;
                case "ToggleVisibleContentsTree": await ChangePageNavigationAsync(c => c.IsFolderTreeVisible = !c.IsFolderTreeVisible); break;
                case "FocusPageListSearchBox":
                    Config.Current.PageList.IsVisibleSearchBox = true; _model.RefreshNavigationPanel(); _model.ShowPanel("PageListPanel"); UpdateLayout(); var pageSearch = this.FindControl<TextBox>("PageListSearchBox")!; pageSearch.Focus(); pageSearch.SelectAll(); break;
                case "FocusFolderSearchBox":
                    Config.Current.Bookshelf.IsVisibleSearchBox = true; _model.RefreshNavigationPanel(); _model.ShowPanel("FolderPanel"); UpdateLayout(); var folderSearch = this.FindControl<TextBox>("FolderSearchBox")!; folderSearch.Focus(); folderSearch.SelectAll(); break;
                case "FocusBookmarkSearchBox":
                    _model.ShowPanel("BookmarkPanel"); this.FindControl<BookmarkListView>("BookmarkPanelList")!.FocusSearch(); break;
                case "FocusBookmarkList": await FocusBookshelfBookmarksAsync(fromMenu); break;
                case "OpenBookExplorer": if (_model.Operation.Book is { } source) await _platform!.RevealAsync(source.Path); break;
                case "ToggleVisibleBookshelf": _model.SelectPanel("FolderPanel"); break;
                case "ToggleVisiblePageList": _model.SelectPanel("PageListPanel"); break;
                case "ToggleVisibleHistoryList": _model.SelectPanel("HistoryPanel"); break;
                case "ToggleVisibleFileInfo": _model.SelectPanel("FileInformationPanel"); break;
                case "ToggleVisibleBookmarkList": _model.SelectPanel("BookmarkPanel"); break;
                case "ToggleVisibleNavigator": _model.SelectPanel("NavigatePanel"); break;
                case "ToggleVisiblePlaylist": _model.SelectPanel("PlaylistPanel"); break;
                case "TogglePlaylistItem": await _model.Operation.TogglePlaylistItemAsync(fromMenu); break;
                case "PrevPlaylist": case "NextPlaylist": await _model.Commands.ExecuteAsync(name); await _model.Operation.SaveAsync(); break;
                case "ToggleVisibleFilmStrip": Config.Current.FilmStrip.IsEnabled = !Config.Current.FilmStrip.IsEnabled; _model.RefreshPanels(); await FilmStrip.RefreshAsync(); break;
                case "ToggleHideFilmStrip": Config.Current.FilmStrip.IsHideFilmStrip = !Config.Current.FilmStrip.IsHideFilmStrip; _model.RefreshPanels(); break;
                default: await _model.Commands.ExecuteAsync(name); break;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
        _autoHide?.Refresh(); UpdatePanelColumns();
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
        RefreshHistoryCommandStates();
    }
    /// <summary>标记回报只刷新菜单，显示控件自行重绘，不触发正文解码。</summary>
    private void Model_MarkersChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    { if (!_preparing && !_closedPrepared) { MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck); RefreshHistoryCommandStates(); } });
    /// <summary>业务回报刷新查看器；只在来源改变时更新目录导航。</summary>
    private async void Model_Refreshed(object? sender, EventArgs e)
    {
        if (_preparing || _model is null) return;
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
        // 书籍变化时立即发起同步；不能等图像解码后再覆盖用户已经进入的书架目录。
        RefreshHistoryCommandStates();
        // 后续手动导航会由 Bookshelf 的原请求代次取消此次同步，图像与目录互不等待。
        _autoHide?.Refresh(); UpdatePanelColumns();
        var folders = RefreshBookshelfAsync();
        await Viewer.RefreshAsync();
        await FilmStrip.RefreshAsync(); await NavigatorView.RefreshAsync();
        await folders;
    }
    /// <summary>只在来源改变时同步书架，独立观察枚举失败和取消，不阻塞图像显示。</summary>
    private async Task RefreshBookshelfAsync()
    {
        if (_preparing || _model is null) return;
        var book = _model.Operation.Book;
        var restored = await _model.Operation.RestoreBookshelfAsync();
        if (book is null || ReferenceEquals(_folderBook, book)) return;
        _folderBook = book; _folders?.Cancel(); var pending = new CancellationTokenSource(); _folders = pending;
        try
        {
            if (restored && !_restoredBookshelfHonored && _model.Operation.Bookshelf.Place is not null) { _restoredBookshelfHonored = true; return; }
            var preserve = _bookmarkOpenTarget == book.Path || _model.Operation.Bookshelf.IsBookmarkPlace && _model.Operation.Bookshelf.Items.Any(item => item.Path == book.Path);
            _bookmarkOpenTarget = null;
            if (preserve && !_model.Operation.Bookshelf.IsBookmarkPlace) return;
            await _model.Operation.Bookshelf.SyncAsync(book, pending.Token, fileSystem: !preserve);
            if (!_preparing && !_closedPrepared) MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (ReferenceEquals(_folderBook, book) && !pending.IsCancellationRequested) ShowError("目录暂不可访问：" + ex.Message); }
        finally { if (ReferenceEquals(_folders, pending)) _folders = null; pending.Dispose(); }
    }
    /// <summary>菜单标识与输入映射共用同一执行入口。</summary>
    private async void Command_Click(object? sender, RoutedEventArgs e) { if (sender is Control { Tag: string name }) await ExecuteAsync(name); }
    /// <summary>侧栏图标改变表现状态，未迁移面板在 XAML 中禁用。</summary>
    private void Panel_Click(object? sender, RoutedEventArgs e) { if (sender is Control { Tag: string name }) _model?.SelectPanel(name); }
    /// <summary>地址栏回车打开，文本编辑不触发阅读键位。</summary>
    private async void Address_KeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter && _model is not null) { e.Handled = true; await OpenAsync(_model.Address); } }
    /// <summary>展示选择转交同一BookOperation；失败回显已提交配置，不维护第二个模式状态。</summary>
    private async void BrowseMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_preparing || _model is null || sender is not ComboBox { SelectedItem: ViewModels.BrowseModeChoice choice } || choice.Mode == _model.Operation.BrowseMode) return;
        try { await _model.Operation.SetBrowseModeAsync(choice.Mode); }
        catch (Exception ex) { _model.Refresh(); ShowError(ex.Message); }
    }
    /// <summary>目录双击统一打开，控件只持有只读条目。</summary>
    private async void Folder_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: FolderItem item })
            try { await OpenBookshelfItemAsync(item); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>在已有元数据上应用原排序，绑定回报和程序同步不重复重排。</summary>
    private async void FolderOrder_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_model is null || sender is not ComboBox { SelectedItem: FolderOrderChoice choice } || choice.Mode == _model.Operation.Bookshelf.FolderOrder) return;
        try { await _model.Operation.ChangeFolderOrderAsync(choice.Mode); MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>历史打开继续走原恢复策略，失败保持当前书籍。</summary>
    private async void History_DoubleTapped(object? sender, TappedEventArgs e)
    { if (Config.Current.Panels.OpenWithDoubleClick && HistoryRowFromSource(e.Source) is { } row) await OpenHistoryAsync(row.Path); }
    /// <summary>文件夹保留展开交互，只打开实际双击的书籍节点。</summary>
    private async void Bookmark_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // 展开箭头可能保持旧书籍选择；不能用 SelectedItem 将箭头双击误解释为重新打开书籍。
        if (_preparing || _closedPrepared || e.Source is Visual source && source.GetVisualAncestors().Prepend(source).OfType<Button>().Any()) return;
        if (BookmarkRow(e.Source)?.DataContext is BookmarkNode { IsFolder: false, Path: { } path }) await OpenAsync(path);
    }
    /// <summary>原列表在当前浏览目录新建；编辑树采用所选文件夹或书签的真实父级。</summary>
    private async void Bookmark_NewFolder(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var parent = _model.SelectedBookmark;
        parent = _bookmarkTreeSelection && parent is not null
            ? parent.IsFolder ? parent : _model.SaveData.Bookmarks.ParentOf(parent)
            : this.FindControl<BookmarkListView>("BookmarkPanelList")!.Navigation?.Place;
        var name = await AskNameAsync("新建书签文件夹", "");
        if (name is null || _preparing || _closedPrepared) return;
        try { SelectBookmark(await _model.SaveData.AddBookmarkFolderAsync(parent?.IsFolder == true ? parent : null, name)); }
        catch (Exception ex) { if (parent is not null && _model.SaveData.BookmarkRoot.Walk().Contains(parent)) SelectBookmark(parent); ShowError(ex.Message); }
    }
    /// <summary>修改所选节点名称，取消不写入状态。</summary>
    private async void Bookmark_Rename(object? sender, RoutedEventArgs e)
    {
        if (_model?.SelectedBookmark is not BookmarkNode node) return;
        var name = await AskNameAsync("重命名书签", node.DisplayName);
        if (name is null || _preparing || _closedPrepared) return;
        try { SelectBookmark(await _model.SaveData.RenameBookmarkAsync(node, name)); }
        catch (BookmarkMergeRequiredException merge)
        {
            // 合并预检回滚也会重建容器；取消确认后仍保留原节点及其展开祖先。
            if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node);
            if (!await ConfirmAsync("合并书签文件夹", $"合并到“{merge.Target.DisplayName}”？同名子文件夹会递归合并，名称和路径均相同的书签保留一份。", "合并")) return;
            if (_preparing || _closedPrepared) return;
            try { SelectBookmark(await _model.SaveData.RenameBookmarkAsync(node, name, confirmedTarget: merge.Target)); }
            catch (Exception ex) { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); ShowError(ex.Message); }
        }
        catch (Exception ex) { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); ShowError(ex.Message); }
    }
    /// <summary>移除树记录；文件夹包含子项时明确确认范围。</summary>
    private async void Bookmark_Remove(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var selected = SelectedBookmarkNodes();
        var fromTree = _bookmarkTreeSelection;
        if (selected.Length == 0) return;
        var message = selected.Length == 1 ? $"移除“{selected[0].DisplayName}”{(selected[0].IsFolder ? "及其全部书签子项" : "")}？" : $"移除选中的 {selected.Length} 项及文件夹内的全部书签？源文件不受影响。";
        if (!await ConfirmAsync("移除书签", message)) return;
        if (_preparing || _closedPrepared) return;
        try { await _model.SaveData.RemoveBookmarksAsync(selected); }
        catch (Exception ex)
        {
            if (!_preparing && !_closedPrepared)
            {
                if (fromTree)
                {
                    var tree = this.FindControl<TreeView>("BookmarkTree")!; tree.SelectedItems.Clear();
                    foreach (var node in selected.Where(e => _model.SaveData.BookmarkRoot.Walk().Contains(e))) tree.SelectedItems.Add(node);
                }
                else this.FindControl<BookmarkListView>("BookmarkPanelList")!.RestoreSelection(selected);
            }
            ShowError(ex.Message);
        }
    }
    /// <summary>小型命名对话框仅返回用户文本，业务校验由 Engine 完成。</summary>
    private Task<string?> AskNameAsync(string title, string value, bool selectStem = false)
    {
        var input = new TextBox { Text = value }; var cancel = new Button { Content = "取消" }; var save = new Button { Content = "确定" };
        var dialog = new Window { Title = title, Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { input, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, save } } } } };
        cancel.Click += (_, _) => dialog.Close(null); save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.Close(input.Text.Trim()); };
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text)) { e.Handled = true; dialog.Close(input.Text.Trim()); } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectionStart = 0; input.SelectionEnd = selectStem ? System.IO.Path.GetFileNameWithoutExtension(value).Length : value.Length; };
        return dialog.ShowDialog<string?>(this);
    }
    /// <summary>范围明确的确认对话框，关闭或取消返回 false。</summary>
    private Task<bool> ConfirmAsync(string title, string message, string acceptText = "移除")
    {
        var cancel = new Button { Content = "取消" }; var accept = new Button { Content = acceptText };
        var dialog = new Window { Title = title, Width = 420, MinHeight = 160, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 16, Children = { new ScrollViewer { MaxHeight = 280, Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap } }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, accept } } } } };
        cancel.Click += (_, _) => dialog.Close(false); accept.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(this);
    }
    /// <summary>按原命令的一起始页码定位，超出范围由 Engine 校正。</summary>
    /// <param name="number">用户页码，1 为首页。</param>
    public Task JumpPageAsync(int number) => _model?.Operation.JumpAsync((int)Math.Clamp((long)number - 1, 0, int.MaxValue)) ?? Task.CompletedTask;
    /// <summary>指定页对话框只接受整数；取消与无效输入不会改动阅读位置。</summary>
    private Task<int?> AskPageNumberAsync()
    {
        var input = new TextBox { Text = (_model!.PageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var message = new TextBlock { Text = $"页码（1–{_model.Pages.Count}），超出范围定位到首尾页。" };
        var cancel = new Button { Content = "取消" }; var accept = new Button { Content = "跳转" };
        var dialog = new Window { Title = "跳转到指定页", Width = 380, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { input, message, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, accept } } } } };
        // Enter 与按钮使用同一确认逻辑，焦点保持在输入作用域。
        void Confirm() { if (int.TryParse(input.Text, out var number)) dialog.Close((int?)number); else message.Text = "请输入整数页码。"; }
        cancel.Click += (_, _) => dialog.Close(null); accept.Click += (_, _) => Confirm();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Confirm(); } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog<int?>(this);
    }
    /// <summary>按下后开启原拖动选择，正文仍保持当前帧。</summary>
    private void Slider_Pressed(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _sliderDragging = true; }
    /// <summary>鼠标释放确认联动选择；取消/正文更新不会自动确认。</summary>
    private async void Slider_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!_sliderDragging) return; _sliderDragging = false;
        try { await CommitSliderAsync(); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>原滑条滚轮默认直接移动正文帧，命令模式复用键鼠绑定入口。</summary>
    private async void Slider_Wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (_model is null || _preparing || _closedPrepared) return;
        if (Config.Current.Slider.MouseWheelAction == SliderMouseWheelAction.CommandDependent)
        { FilmStrip_GlobalWheel(sender, e); return; }
        _sliderWheel += e.Delta.Y; var steps = (int)_sliderWheel; _sliderWheel -= steps;
        try { for (int i = 0; i < Math.Abs(steps); i++) await _model.Operation.MoveAsync(steps > 0 ? -1 : 1); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>使用原双页对齐规则改变临时选择；未联动时立即定位。</summary>
    public async Task PreviewSliderAsync(int index)
    {
        if (_model is null || _preparing || _model.Pages.Count == 0) return;
        var operation = _model.Operation;
        index = operation.FilmStrip.GetFixedSliderIndex(index, operation.Frame?.Elements.Count(e => !e.IsDummy) ?? 0);
        operation.PageSelector.SetSelectedIndex(this, index, true);
        if (!Config.Current.FilmStrip.IsEnabled || !Config.Current.Slider.IsSliderLinkedFilmStrip) await operation.JumpAsync(operation.PageSelector.SelectedIndex);
    }
    /// <summary>滑条释放或 Enter 将共用选择提交到原正文定位。</summary>
    public Task CommitSliderAsync() => _model is not null && Config.Current.FilmStrip.IsEnabled && Config.Current.Slider.IsSliderLinkedFilmStrip
        ? _model.Operation.JumpAsync(_model.Operation.PageSelector.SelectedIndex) : Task.CompletedTask;
    /// <summary>程序绑定与用户拖动分开，避免选择回报形成重复跳页。</summary>
    private async void Slider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_model is null || _preparing || _sliderUpdating || Math.Abs(e.NewValue - _model.PageIndex) < .5) return;
        // 绑定刷新不属于输入；焦点内的键盘调整与指针拖动才进入临时选择。
        if (!_sliderDragging && FocusManager?.GetFocusedElement() != sender) return;
        _sliderUpdating = true;
        try { await PreviewSliderAsync((int)Math.Round(e.NewValue)); _model.RefreshSelection(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _sliderUpdating = false; }
    }
    /// <summary>系统 Command 键先处理；编辑控件隔离其余原快捷键。</summary>
    private async void Key_Down(object? sender, KeyEventArgs e)
    {
        // 原 PreviewKeyDown 先 LeaveVisibleLocked，避免委托注册顺序让新锁被同一按键清除。
        _autoHide?.HandleKey(e.Source as Control);
        if (e.Handled || _model is null) return;
        var inputWindow = sender as Window ?? this;
        var focusedElement = inputWindow.FocusManager?.GetFocusedElement();
        if (e.KeyModifiers == KeyModifiers.Meta && e.Key is Key.O or Key.W or Key.Q)
        { e.Handled = true; if (e.Key == Key.W && inputWindow is FloatingPanelWindow) inputWindow.Close(); else await ExecuteAsync(e.Key == Key.O ? "LoadAs" : e.Key == Key.W ? "CloseWindow" : "CloseApplication"); return; }
        // 原生popup可使用独立窗口，主窗FocusManager不一定返回ComboBox或菜单项。
        // 这里只退出全局命令匹配，不设置Handled，控件仍收到自己的导航/确认/取消键。
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Enter or Key.Space or Key.Escape
            && HasPopupInputScope(inputWindow, focusedElement as Control)) return;
        if (e.Key == Key.Escape && (Viewer.CancelMouseSequence() || _sidePanels?.CancelDrag() == true)) { e.Handled = true; return; }
        if (focusedElement is TextBox) return;
        // 弹出菜单拥有方向键，不能让原阅读快捷键抢走菜单导航/选择。
        if (this.FindControl<Menu>("MenuBar")!.IsOpen || focusedElement is MenuItem) return;
        if (focusedElement is ComboBox && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Space) return;
        // 普通列表拥有方向/定位键；选条目不能触发全局 Up/Down 书籍导航。
        if (focusedElement is Control focused && (focused is ListBox or TreeView || focused.GetVisualAncestors().Any(x => x is ListBox or TreeView)))
        {
            if (this.FindControl<FolderTreeView>("BookshelfDirectoryTree")!.IsKeyboardFocusWithin && e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter) return;
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter && this.FindControl<ListBox>("PageList")!.IsKeyboardFocusWithin)
            { e.Handled = true; await CommitPageListAsync(); if (Config.Current.PageList.FocusMainView) Viewer.Focus(); return; }
            // 窗口Tunnel先于列表Bubble；显式多选必须在全局DeleteFile匹配前处理。
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete && this.FindControl<ListBox>("PageList")!.IsKeyboardFocusWithin)
            { e.Handled = true; await DeleteSelectedPagesAsync(); return; }
            var bookmarkList = this.FindControl<BookmarkListView>("BookmarkPanelList")!;
            if (bookmarkList.IsKeyboardFocusWithin && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Back) return;
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete && (bookmarkList.IsKeyboardFocusWithin || this.FindControl<TreeView>("BookmarkTree")!.IsKeyboardFocusWithin))
            { e.Handled = true; Bookmark_Remove(this, new RoutedEventArgs()); return; }
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Delete && this.FindControl<PlaylistView>("PlaylistPanelView")!.IsKeyboardFocusWithin) return;
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Delete && this.FindControl<ListBox>("HistoryList")!.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                if (e.Key == Key.Delete) await RemoveSelectedHistoryAsync();
                else if (_model.SelectedHistory is { } history) await OpenHistoryAsync(history.Path);
                return;
            }
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter && this.FindControl<ListBox>("FolderList")!.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                if (_model.SelectedFolder is { } selected)
                    try { await OpenBookshelfItemAsync(selected); } catch (Exception ex) { ShowError(ex.Message); }
                return;
            }
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) return;
        }
        if (focusedElement == FilmStrip && FilmStrip.HandleSelectionKey(e)) return;
        if (focusedElement == this.FindControl<Slider>("PageSliderView") && e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter)
        { e.Handled = true; await CommitSliderAsync(); return; }
        if (focusedElement == this.FindControl<Slider>("PageSliderView") && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down) return;
        var matches = _model.Commands.Definitions.Where(d => _model.SaveData.GetShortcut(d.Name, d.Shortcut).Split(',').Any(s => MatchKey(s, e))).ToArray();
        if (matches.Length == 0) return; e.Handled = true;
        if (matches.Length > 1) { ShowError("快捷键冲突：" + string.Join("、", matches.Select(d => d.Text))); return; }
        await ExecuteInputAsync(matches[0].Name, true);
    }
    /// <summary>检查主窗/浮窗的实际弹出层；tooltip不占键盘作用域，不依赖原生popup的焦点回报。</summary>
    /// <param name="inputWindow">接收键盘路由的主窗口或浮窗。</param><param name="focused">该窗口回报的逻辑焦点。</param>
    /// <returns>交互popup展开时为true；不接管系统Command+O/W/Q。</returns>
    private bool HasPopupInputScope(Window inputWindow, Control? focused)
    {
        static bool HasPopup(Window window) => window.OpenedPopups.Any(p => p.IsOpen && p.Child is not ToolTip);
        if (this.FindControl<ComboBox>("BrowseModeView")?.IsDropDownOpen == true) return true;
        if (focused is ComboBox { IsDropDownOpen: true } || focused?.GetVisualAncestors().OfType<ComboBox>().Any(c => c.IsDropDownOpen) == true) return true;
        return HasPopup(inputWindow) || inputWindow != this && HasPopup(this)
            || _sidePanels?.FloatingWindows.Any(HasPopup) == true;
    }
    /// <summary>与菜单提示共用原数字键转换；旧 Control 不转换为 Command。</summary>
    /// <param name="value">单个原快捷键。</param><param name="e">当前真实键盘输入。</param>
    /// <returns>键及修饰键完全匹配时为 true；无效或鼠标手势为 false。</returns>
    internal static bool MatchKey(string value, KeyEventArgs e) => KeyboardGestureParser.TryParse(value)?.Matches(e) == true;
    /// <summary>沿原方向字典登记顺序后项覆盖；精确匹配且保留未迁命令能力提示。</summary>
    private CommandDefinition? FindMouseSequence(MouseSequence sequence) => sequence.IsEmpty || _model is null ? null
        : _model.Commands.Definitions.LastOrDefault(d => _model.SaveData.GetMouseGesture(d.Name, d.MouseGesture) == sequence);
    /// <summary>鼠标手势按原差分绑定匹配，冲突不会静默覆盖。</summary>
    private async Task GestureAsync(string gesture, bool allowReverse = true)
    {
        var matches = GetGestureMatches(gesture);
        if (matches.Length == 1) await ExecuteInputAsync(matches[0].Name, allowReverse);
        else if (matches.Length > 1) ShowError("输入冲突：" + string.Join("、", matches.Select(d => d.Text)));
    }
    /// <summary>仅输入路径应用原按滑条方向的成对交换；菜单执行保留明确命令含义。</summary>
    private Task ExecuteInputAsync(string name, bool allowReverse)
    {
        if (_model is null) return Task.CompletedTask;
        var parameter = _model.SaveData.GetCommandParameter<ReversibleCommandParameter>(name);
        var resolved = DefaultInputScheme.ResolveCommand(name, Config.Current.Command, !_model.SliderReversed, allowReverse, parameter.IsReverse);
        return ExecuteAsync(resolved);
    }
    /// <summary>使用同一规范解析匹配修饰集合；未知动作不被普通点击代替。</summary>
    private CommandDefinition[] GetGestureMatches(string gesture)
    {
        if (_model is null || _preparing || _closedPrepared || !MouseGestureSource.TryNormalize(gesture, out var normalized)) return [];
        return _model.Commands.Definitions.Where(d => _model.SaveData.GetShortcut(d.Name, d.Shortcut).Split(',')
            .Any(s => MouseGestureSource.TryNormalize(s, out var candidate) && candidate == normalized)).ToArray();
    }
    /// <summary>按下阶段先裁决Handled，执行仍走统一异步命令入口。</summary>
    private bool TryHandleGesture(string gesture)
    {
        if (GetGestureMatches(gesture).Length == 0) return false;
        _ = GestureAsync(gesture); return true;
    }
    /// <summary>命令依赖模式按原修饰键滚轮绑定执行，不绕过配置强制缩放/翻页。</summary>
    private async void FilmStrip_GlobalWheel(object? sender, PointerWheelEventArgs e)
    {
        await DispatchWheelAsync(sender as Control ?? FilmStrip, e);
    }
    /// <summary>两轴/输入作用域/修饰分别积累；一次多格滚轮不丢步，也不跨修饰拼接。</summary>
    private async Task DispatchWheelAsync(Control scope, PointerWheelEventArgs e)
    {
        var properties = e.GetCurrentPoint(scope).Properties;
        foreach (var (delta, negative, positive, horizontal) in new[] { (e.Delta.Y, "WheelDown", "WheelUp", false), (e.Delta.X, "WheelLeft", "WheelRight", true) })
        {
            if (delta == 0) continue;
            var gesture = MouseGestureSource.Create(delta < 0 ? negative : positive, e.KeyModifiers, properties);
            if (!MouseGestureSource.TryNormalize(gesture, out var normalized)) continue;
            var key = ((object)scope, normalized[..normalized.LastIndexOf("Wheel", StringComparison.Ordinal)] + (horizontal ? "Horizontal" : "Vertical"));
            var total = _wheelDeltas.GetValueOrDefault(key) + delta; var signedSteps = (int)total; _wheelDeltas[key] = total - signedSteps;
            if (signedSteps == 0) continue;
            gesture = MouseGestureSource.Create(signedSteps < 0 ? negative : positive, e.KeyModifiers, properties);
            var steps = horizontal && Config.Current.Command.IsHorizontalWheelLimitedOnce ? Math.Min(Math.Abs(signedSteps), 1) : Math.Abs(signedSteps);
            if (ReferenceEquals(scope, Viewer) && GetGestureMatches(gesture).Length > 0) Viewer.SuppressPendingClick(properties);
            for (int i = 0; i < steps; i++) await GestureAsync(gesture, horizontal ? Config.Current.Command.IsReversePageMoveHorizontalWheel : Config.Current.Command.IsReversePageMoveWheel);
        }
    }
    /// <summary>查看器菜单复用原完整菜单树和占位；右键方案调用同一命令入口。</summary>
    private void OpenViewerContextMenu()
    {
        if (_model is null) return;
        var menu = new Menu(); MenuPresenter.Populate(menu, MenuTree.CreateDefault(), _model.Commands, _model.SaveData, IsCommandAvailable, name => ExecuteAsync(name, true), GetCommandCheck);
        var items = menu.Items.Cast<object>().ToArray(); menu.Items.Clear();
        Viewer.ContextMenu?.Close();
        var context = new ContextMenu { ItemsSource = items }; Viewer.ContextMenu = context;
        context.Closed += (_, _) => { if (ReferenceEquals(Viewer.ContextMenu, context)) Viewer.ContextMenu = null; };
        context.Open(Viewer);
    }
    /// <summary>普通滚轮按原绑定；正式 Mac 精确滚动由平台桥接提前消费。</summary>
    private async void Viewer_Wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (!Viewer.TryWheelScroll(e)) await DispatchWheelAsync(Viewer, e);
    }
    /// <summary>Finder 拖入使用与菜单相同的打开链路。</summary>
    private async void Drop(object? sender, DragEventArgs e)
    {
        if (e.Handled || _model is null || _preparing || _closedPrepared) return;
        try
        {
            var content = ContentDropSnapshot.Read(e.DataTransfer);
            if (content.Files.Count == 0 && content.QueryPaths.Count == 0 && content.Content is null) return;
            e.Handled = true; await RunDestinationActionAsync(() => _model.Operation.OpenDroppedContentAsync(content));
        }
        catch (Exception ex) { e.Handled = true; ShowError(ex.Message); }
    }
    /// <summary>显示完整基线命令及迁移状态，已登记数量不当作功能覆盖率。</summary>
    private Task ShowCommandStatusAsync()
    {
        var text = string.Join('\n', _model!.Commands.Definitions.Select(d => $"{d.Text}  [{d.Name}]  {(IsCommandImplemented(d.Name) ? "已接入，详见验收记录" : d.Stage)}  {_model.SaveData.GetShortcut(d.Name, d.Shortcut)}"));
        return new Window { Title = "命令迁移状态", Width = 680, Height = 600, Content = new ScrollViewer { Content = new TextBlock { Text = text, Margin = new Thickness(16) } } }.ShowDialog(this);
    }
    /// <summary>错误信息只改变表现，不覆盖当前阅读内容。</summary>
    private void ShowError(string message) { if (!_preparing && !_closedPrepared) this.FindControl<TextBlock>("StatusField")!.Text = message; }
    /// <summary>Mac 全屏进入前保留普通/最大化状态；取消恢复真实上一状态。</summary>
    private void SetFullScreen(bool enabled)
    {
        if (enabled && WindowState != WindowState.FullScreen)
        { _lastFullScreenState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal; WindowState = WindowState.FullScreen; }
        else if (!enabled && WindowState == WindowState.FullScreen) WindowState = _lastFullScreenState;
    }
    /// <summary>正常关闭先释放需求并保存，失败保留窗口供重试。</summary>
    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closedPrepared) return; e.Cancel = true;
        try { await PrepareShutdownAsync(); Close(); } catch (Exception ex) { _preparing = false; ShowError("关闭失败：" + ex.Message); }
    }
    /// <summary>统一关闭和退出准备；保存分隔宽度及原阅读状态。</summary>
    public Task PrepareShutdownAsync()
    {
        if (_closedPrepared) return Task.CompletedTask;
        return _shutdown ??= ShutdownCoreAsync();
    }
    /// <summary>共享关闭任务；保存失败允许重试，并保持查看器及当前来源可用。</summary>
    private async Task ShutdownCoreAsync()
    {
        await Task.Yield();
        _preparing = true;
        _model?.Operation.CancelClipboardPreparation();
        _model?.Operation.CancelFileCopyPreparation();
        _sidePanels?.PrepareClose();
        _pageEndDialog?.Close(PageEndAction.None);
        try
        {
            foreach (var dialog in OwnedWindows.ToArray()) dialog.Close();
            _model?.Operation.CancelBookTransferPreparation();
            await _destinationAction;
            await this.FindControl<DestinationFolderPanelView>("DestinationPanelView")!.PrepareCloseAsync();
            await _listStyleTask;
            await _folderTreeSettingsTask;
            await _quickAccessEditTask;
            await _pageNavigationSettings;
            _contentsRequest?.Cancel();
            _historyCleanupCancellation?.Cancel();
            if (_historyCleanupTask is not null) await _historyCleanupTask;
            await this.FindControl<PlaylistView>("PlaylistPanelView")!.PrepareCloseAsync();
            await this.FindControl<BookmarkListView>("BookmarkPanelList")!.PrepareCloseAsync();
            if (_model is not null) await _model.HistorySearch.PrepareCloseAsync();
            if (_model is not null) { await _model.PageSearch.PrepareCloseAsync(); await _model.FolderSearch.PrepareCloseAsync(); }
            _folders?.Cancel(); _sliderDragging = false; PageNumber.CancelEdit();
            CancelBookmarkDrag();
            if (_model is not null)
            {
                var left = this.FindControl<Border>("LeftPanel")!.Bounds.Width;
                var right = this.FindControl<Border>("RightPanel")!.Bounds.Width;
                if (left > 0) Config.Current.Panels.LeftWidth = left;
                if (right > 0) Config.Current.Panels.RightWidth = right;
                _sidePanels?.SaveWeights();
                // 先完成可靠保存，再退订与释放显示资源；失败不能留下已销毁的阅读窗口。
                await _model.Operation.DisposeAsync();
                _model.Operation.Bookshelf.Changed -= FolderTree_PlaceChanged;
                _model.Operation.PageEndDialogAsync = null;
                _model.Operation.ConfirmDeleteAsync = null;
                _model.Operation.AskBookNameAsync = null; _model.Operation.ConfirmBookRenameAsync = null; _model.Operation.RetryBookRenameAsync = null;
                _model.HistoryRefreshed -= History_Refreshed;
                _model.Operation.MarkersChanged -= Model_MarkersChanged;
                if (_model.Operation.DestinationMoves is { } moves) { moves.StateChanged -= DestinationMove_Changed; moves.ConfirmOverwriteAsync = null; moves.ConfirmDirectoryOverwriteAsync = null; }
                _model.Detach(); _model.Refreshed -= Model_Refreshed; _model.PanelsRefreshed -= Model_PanelsRefreshed; _model.ChromeRefreshed -= Model_ChromeRefreshed;
                _model.Refreshed -= PageNavigation_Refreshed;
            }
            this.FindControl<PlaylistView>("PlaylistPanelView")!.Dispose();
            this.FindControl<DestinationFolderPanelView>("DestinationPanelView")!.Dispose();
            this.FindControl<BookmarkListView>("BookmarkPanelList")!.Dispose();
            _autoHide?.Dispose(); _sidePanels?.Dispose(); _platformInput?.Dispose(); FilmStrip.Dispose(); NavigatorView.Dispose(); Viewer.Dispose(); _images?.Dispose(); _closedPrepared = true;
        }
        finally
        {
            if (!_closedPrepared) { _sidePanels?.CancelClose(); this.FindControl<DestinationFolderPanelView>("DestinationPanelView")!.CancelClose(); this.FindControl<PlaylistView>("PlaylistPanelView")!.CancelClose(); this.FindControl<BookmarkListView>("BookmarkPanelList")!.CancelClose(); _model?.HistorySearch.CancelClose(); _model?.PageSearch.CancelClose(); _model?.FolderSearch.CancelClose(); }
            _preparing = false; _shutdown = null;
        }
    }
}
