using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using NeeView;
using NeeView.Runtime.LayoutPanel;
namespace NeeView.MacOS.ViewModels;

/// <summary>窗口表现状态；视图可独立调整，所有阅读动作进入原 BookOperation。</summary>
public sealed class ReaderWorkspaceViewModel(BookOperation operation, CommandTable commands, SaveData saveData)
    : ObservableObject
{
    public LayoutPanelManager Layout { get; } = new(Config.Current.Panels.Layout);
    public bool IsPanelDragging { get; private set; }
    public BookOperation Operation { get; } = operation;
    public CommandTable Commands { get; } = commands;
    public SaveData SaveData { get; } = saveData;
    public string Title => Operation.Book is { } book ? $"{System.IO.Path.GetFileName(book.Path)} — NeeView" : "NeeView";
    private string _address = "";
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public IReadOnlyList<Page> Pages => Operation.Book is { } book ? book.Pages : [];
    private Page? _selectedPage;
    public Page? SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }
    public IReadOnlyList<FolderItem> Folders => Operation.Bookshelf.Items;
    public string FolderPlace => Operation.Bookshelf.Place ?? "";
    public string FolderMessage => Operation.Bookshelf.Error ?? (Operation.Bookshelf.IsLoading ? "正在读取目录…" : "");
    private bool _refreshingFolders;
    private FolderItem? _selectedFolder;
    public FolderItem? SelectedFolder
    {
        get => _selectedFolder;
        set { if (SetProperty(ref _selectedFolder, value) && !_refreshingFolders) Operation.Bookshelf.Select(value); }
    }
    public static IReadOnlyList<FolderOrderChoice> FolderOrders { get; } =
    [new(FolderOrder.FileName, "文件名"), new(FolderOrder.FileNameDescending, "文件名（降序）"),
     new(FolderOrder.FileType, "类型"), new(FolderOrder.FileTypeDescending, "类型（降序）"),
     new(FolderOrder.TimeStamp, "时间"), new(FolderOrder.TimeStampDescending, "时间（降序）"),
     new(FolderOrder.Size, "大小"), new(FolderOrder.SizeDescending, "大小（降序）"), new(FolderOrder.Random, "随机")];
    public FolderOrderChoice SelectedFolderOrder => FolderOrders.First(e => e.Mode == Operation.Bookshelf.FolderOrder);
    public int LastIndex => Math.Max(0, Pages.Count - 1);
    public double PageIndex => Operation.PageSelector.SelectedIndex;
    public bool SliderReversed => Operation.FilmStrip.IsSliderDirectionReversed;
    public double FilmStripHeight => Config.Current.FilmStrip.ImageWidth + 16;
    public string PositionText => Pages.Count == 0 ? "0 / 0" : $"{PageIndex + 1} / {Pages.Count}";
    public bool IsLoading => Operation.IsLoading;
    public string Status => Operation.Error ?? (Operation.Book is { } book ? $"{book.CurrentPage?.EntryName}  ·  {(book.Setting.PageMode == PageMode.WidePage ? "双页" : "单页")}  ·  {(book.Setting.BookReadOrder == PageReadOrder.RightToLeft ? "从右向左" : "从左向右")}" : "打开图片、目录或 ZIP / CBZ");
    public string Information => Operation.Book?.CurrentPage is { } page ? $"{page.EntryName}\n\n尺寸：{page.Content.PageDataSource.Size.Width:0} × {page.Content.PageDataSource.Size.Height:0}\n大小：{page.ArchiveEntry.Length:N0} 字节\n\n来源：{Operation.Book.Path}\n{page.Content.Error}" : "没有打开书籍";
    public bool Divide => Operation.Book?.Setting.IsSupportedDividePage ?? false;
    public bool Wide => Operation.Book?.Setting.IsSupportedWidePage ?? true;
    public bool FirstSingle => Operation.Book?.Setting.IsSupportedSingleFirstPage ?? false;
    public bool LastSingle => Operation.Book?.Setting.IsSupportedSingleLastPage ?? false;
    public double LeftWidth => Config.Current.Panels.LeftWidth;
    public double RightWidth => Config.Current.Panels.RightWidth;
    public bool LeftVisible => (Config.Current.Panels.IsLeftVisible || IsPanelDragging) && Layout.Docks["Left"].Items.Count > 0 && (!LeftAutoHide || _leftHovered || IsPanelDragging);
    public bool RightVisible => (Config.Current.Panels.IsRightVisible || IsPanelDragging) && Layout.Docks["Right"].Items.Count > 0 && (!RightAutoHide || _rightHovered || IsPanelDragging);
    public bool LeftAutoHide => Config.Current.Panels.IsLeftAutoHide;
    public bool RightAutoHide => Config.Current.Panels.IsRightAutoHide;
    private bool _leftHovered, _rightHovered;
    public bool ShowPageList => IsPanelVisible("PageListPanel");
    public bool ShowFolderList => IsPanelVisible("FolderPanel");
    public bool ShowHistory => IsPanelVisible("HistoryPanel");
    public bool ShowInformation => IsPanelVisible("FileInformationPanel");
    public bool ShowBookmarks => IsPanelVisible("BookmarkPanel");
    public bool ShowNavigator => IsPanelVisible("NavigatePanel");
    /// <summary>跨栏后按实际组选择与栏显隐计算面板状态。</summary>
    public bool IsPanelVisible(string name) => Layout.Find(name) is { } found && ReferenceEquals(Layout.Docks[found.Side].SelectedItem, found.Group) && (found.Side == "Left" ? LeftVisible : RightVisible);
    public string HistorySearch { get => Operation.HistoryList.SearchKeyword; set { if (value == HistorySearch) return; Operation.HistoryList.SearchKeyword = value; OnPropertyChanged(); RefreshHistory(); } }
    private IReadOnlyList<HistoryRow> _historyRows = [];
    public IReadOnlyList<HistoryRow> History => _historyRows;
    private HistoryRow? _selectedHistory;
    public HistoryRow? SelectedHistory { get => _selectedHistory; set => SetProperty(ref _selectedHistory, value); }
    public bool HistorySearchVisible => Config.Current.History.IsVisibleSearchBox;
    public bool HistoryCountVisible => Config.Current.History.IsVisibleItemsCount;
    public string HistoryPlace => Operation.HistoryList.FilterPath ?? "全部历史记录";
    public string HistoryCount => $"{History.Count} / {SaveData.HistoryEntries.Count} 项";
    private Book? _historyBook;
    public event EventHandler? HistoryRefreshed;
    public IReadOnlyList<BookmarkNode> Bookmarks => SaveData.BookmarkRoot.Children ?? [];
    private BookmarkNode? _selectedBookmark;
    public BookmarkNode? SelectedBookmark { get => _selectedBookmark; set => SetProperty(ref _selectedBookmark, value); }
    public bool CanRestoreBookmarks => SaveData.CanRestoreBookmarks;
    private int _bookmarkSelectionCount;
    public int BookmarkSelectionCount
    {
        get => _bookmarkSelectionCount;
        set { if (SetProperty(ref _bookmarkSelectionCount, value)) { OnPropertyChanged(nameof(IsSingleBookmarkSelection)); OnPropertyChanged(nameof(CanChooseBookmarkFolder)); } }
    }
    public bool IsSingleBookmarkSelection => BookmarkSelectionCount == 1;
    public bool CanChooseBookmarkFolder => BookmarkSelectionCount <= 1;
    public bool IsBookmark => Operation.Book is { } book && SaveData.IsBookmark(book.Path);
    public bool FilmStripVisible => Config.Current.FilmStrip.IsEnabled && (!Config.Current.FilmStrip.IsHideFilmStrip || _filmHovered) && Pages.Count > 0;
    private bool _filmHovered;
    public event EventHandler? Refreshed;
    public event EventHandler? PanelsRefreshed;

    /// <summary>装配业务订阅；后台回报统一切 UI 线程。</summary>
    public void Attach() { Layout.Changed += Layout_Changed; Operation.Changed += Operation_Changed; Operation.PageSelector.SelectionChanged += Selection_Changed; Operation.Bookshelf.Changed += Bookshelf_Changed; SaveData.Changed += SaveData_Changed; Refresh(); RefreshFolders(); }
    /// <summary>关闭窗口时解除订阅，避免旧窗口收到新书变化。</summary>
    public void Detach() { Layout.Changed -= Layout_Changed; Operation.Changed -= Operation_Changed; Operation.PageSelector.SelectionChanged -= Selection_Changed; Operation.Bookshelf.Changed -= Bookshelf_Changed; SaveData.Changed -= SaveData_Changed; }
    /// <summary>目录回报只更新书架表现，不触发正文或缩略图加载。</summary>
    private void Bookshelf_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshFolders);
    /// <summary>先替换集合再恢复选择；屏蔽列表 TwoWay 清空对引擎选择的回写。</summary>
    private void RefreshFolders()
    {
        _refreshingFolders = true;
        try
        {
            OnPropertyChanged(nameof(Folders)); OnPropertyChanged(nameof(FolderPlace)); OnPropertyChanged(nameof(FolderMessage));
            SelectedFolder = Operation.Bookshelf.SelectedItem; OnPropertyChanged(nameof(SelectedFolderOrder));
        }
        finally { _refreshingFolders = false; }
    }
    /// <summary>临时选择仅通知滑条及编号，不发布正文刷新或改变页面列表当前项。</summary>
    private void Selection_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshSelection);
    /// <summary>配置改变时同步方向和选择表现，前端设置不重新扫描来源。</summary>
    public void RefreshSelection()
    { OnPropertyChanged(nameof(PageIndex)); OnPropertyChanged(nameof(PositionText)); OnPropertyChanged(nameof(SliderReversed)); OnPropertyChanged(nameof(FilmStripHeight)); }
    /// <summary>历史与书签回报只更新导航面板，不重新解码当前帧。</summary>
    private void SaveData_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RefreshHistory(); OnPropertyChanged(nameof(Bookmarks)); OnPropertyChanged(nameof(IsBookmark)); OnPropertyChanged(nameof(CanRestoreBookmarks));
        if (SelectedBookmark is not null && !SaveData.BookmarkRoot.Walk().Contains(SelectedBookmark)) SelectedBookmark = null;
    });
    /// <summary>界面只展示最新业务状态，排队回报不会携带旧书快照。</summary>
    private void Operation_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);
    /// <summary>刷新绑定；页面对象保持原身份，不创建另一套页面模型。</summary>
    public void Refresh()
    {
        Address = Operation.Book?.Path ?? Address;
        // 先替换列表来源，再恢复选择；反向顺序会被 ListBox 的 TwoWay 清空回报覆盖。
        OnPropertyChanged(""); SelectedPage = Operation.Book?.CurrentPage; RefreshHistory(); Refreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>复用未变化的历史展示行，保留列表选择；分组与筛选不刷新正文或读取文件。</summary>
    public void RefreshHistory()
    {
        var selectedPath = !ReferenceEquals(_historyBook, Operation.Book) ? Operation.Book?.Path : SelectedHistory?.Path;
        _historyBook = Operation.Book;
        var old = _historyRows.GroupBy(row => row.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        string? previousGroup = null;
        _historyRows = Operation.HistoryList.GetViewItems().Select(entry =>
        {
            string? group = Config.Current.History.IsGroupBy ? HistoryList.GetGroupName(entry.LastAccessTime, DateTime.Today) : null;
            var header = group == previousGroup ? null : group; previousGroup = group;
            return old.TryGetValue(entry.Path, out var row) && row.Entry == entry && row.GroupHeader == header ? row : new HistoryRow(entry, header);
        }).ToArray();
        OnPropertyChanged(nameof(History));
        SelectedHistory = _historyRows.FirstOrDefault(row => row.Path == selectedPath) ?? _historyRows.FirstOrDefault();
        foreach (var name in new[] { nameof(HistorySearchVisible), nameof(HistoryCountVisible), nameof(HistoryPlace), nameof(HistoryCount) }) OnPropertyChanged(name);
        HistoryRefreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>焦点命令明确显示所需面板，不使用切换语义把已打开面板关闭。</summary>
    public void ShowPanel(string name)
    {
        if (Layout.Find(name) is not { } found) return;
        if (found.Side == "Left") Config.Current.Panels.IsLeftVisible = true; else Config.Current.Panels.IsRightVisible = true;
        Layout.Docks[found.Side].SelectedItem = found.Group;
        Config.Current.Panels.Layout = Layout.CreateMemento(); RefreshPanels();
    }
    /// <summary>切换已支持侧栏页面或显隐，业务阅读位置不变。</summary>
    public void SelectPanel(string name)
    {
        if (Layout.Find(name) is not { } found) return;
        var dock = Layout.Docks[found.Side];
        bool selected = ReferenceEquals(dock.SelectedItem, found.Group);
        if (found.Side == "Left") Config.Current.Panels.IsLeftVisible = !Config.Current.Panels.IsLeftVisible || !selected;
        else Config.Current.Panels.IsRightVisible = !Config.Current.Panels.IsRightVisible || !selected;
        dock.SelectedItem = found.Group;
        Config.Current.Panels.Layout = Layout.CreateMemento(); RefreshPanels();
    }
    /// <summary>拖动锁定两侧自动隐藏，取消或完成后恢复原显隐配置。</summary>
    public void SetPanelDragging(bool value) { IsPanelDragging = value; RefreshPanels(); }
    /// <summary>布局变化只保存原布局节点并刷新表现，不触发阅读解码。</summary>
    private void Layout_Changed(object? sender, EventArgs e) { Config.Current.Panels.Layout = Layout.CreateMemento(); RefreshPanels(); }
    /// <summary>只通知侧栏绑定，不发布阅读刷新或重新申请图像。</summary>
    public void RefreshPanels()
    {
        foreach (var name in new[] { nameof(LeftVisible), nameof(RightVisible), nameof(LeftAutoHide), nameof(RightAutoHide), nameof(ShowPageList), nameof(ShowFolderList), nameof(ShowHistory), nameof(ShowBookmarks), nameof(ShowInformation), nameof(ShowNavigator), nameof(FilmStripVisible) }) OnPropertyChanged(name);
        PanelsRefreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>自动隐藏只改变窗口表现状态，不改变书籍和目录索引。</summary>
    public void Hover(bool left, bool value) { if (left) _leftHovered = value; else _rightHovered = value; RefreshPanels(); }
    /// <summary>胶片条自动隐藏只改变表现，鼠标进入底栏后恢复。</summary>
    public void HoverFilmStrip(bool value) { _filmHovered = value; OnPropertyChanged(nameof(FilmStripVisible)); }
}
/// <summary>原排序枚举的界面文案，展示顺序不改变 JSON 枚举值。</summary>
public sealed record FolderOrderChoice(FolderOrder Mode, string Label);
/// <summary>历史行的独立表现数据；分组标题不成为可导航或删除的伪历史条目。</summary>
public sealed record HistoryRow(HistoryEntry Entry, string? GroupHeader)
{
    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public string? Page => Entry.Page;
    public bool HasGroupHeader => GroupHeader is not null;
}
