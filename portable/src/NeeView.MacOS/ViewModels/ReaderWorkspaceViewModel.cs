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
    public IReadOnlyList<Page> Pages => Operation.Book?.Pages ?? [];
    private Page? _selectedPage;
    public Page? SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }
    private IReadOnlyList<FolderItem> _folders = [];
    public IReadOnlyList<FolderItem> Folders { get => _folders; private set => SetProperty(ref _folders, value); }
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
    private string _historySearch = "";
    public string HistorySearch { get => _historySearch; set { if (SetProperty(ref _historySearch, value)) OnPropertyChanged(nameof(History)); } }
    public IReadOnlyList<HistoryEntry> History => SaveData.HistoryEntries.Where(e => e.Path.Contains(HistorySearch, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    public IReadOnlyList<BookmarkNode> Bookmarks => SaveData.BookmarkRoot.Children ?? [];
    public bool IsBookmark => Operation.Book is { } book && SaveData.IsBookmark(book.Path);
    public bool FilmStripVisible => Config.Current.FilmStrip.IsEnabled && (!Config.Current.FilmStrip.IsHideFilmStrip || _filmHovered) && Pages.Count > 0;
    private bool _filmHovered;
    public event EventHandler? Refreshed;
    public event EventHandler? PanelsRefreshed;

    /// <summary>装配业务订阅；后台回报统一切 UI 线程。</summary>
    public void Attach() { Layout.Changed += Layout_Changed; Operation.Changed += Operation_Changed; Operation.PageSelector.SelectionChanged += Selection_Changed; SaveData.Changed += SaveData_Changed; Refresh(); }
    /// <summary>关闭窗口时解除订阅，避免旧窗口收到新书变化。</summary>
    public void Detach() { Layout.Changed -= Layout_Changed; Operation.Changed -= Operation_Changed; Operation.PageSelector.SelectionChanged -= Selection_Changed; SaveData.Changed -= SaveData_Changed; }
    /// <summary>临时选择仅通知滑条及编号，不发布正文刷新或改变页面列表当前项。</summary>
    private void Selection_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshSelection);
    /// <summary>配置改变时同步方向和选择表现，前端设置不重新扫描来源。</summary>
    public void RefreshSelection()
    { OnPropertyChanged(nameof(PageIndex)); OnPropertyChanged(nameof(PositionText)); OnPropertyChanged(nameof(SliderReversed)); OnPropertyChanged(nameof(FilmStripHeight)); }
    /// <summary>历史与书签回报只更新导航面板，不重新解码当前帧。</summary>
    private void SaveData_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        OnPropertyChanged(nameof(History)); OnPropertyChanged(nameof(Bookmarks)); OnPropertyChanged(nameof(IsBookmark));
    });
    /// <summary>界面只展示最新业务状态，排队回报不会携带旧书快照。</summary>
    private void Operation_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);
    /// <summary>刷新绑定；页面对象保持原身份，不创建另一套页面模型。</summary>
    public void Refresh()
    {
        Address = Operation.Book?.Path ?? Address;
        // 先替换列表来源，再恢复选择；反向顺序会被 ListBox 的 TwoWay 清空回报覆盖。
        OnPropertyChanged(""); SelectedPage = Operation.Book?.CurrentPage; Refreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>接收后端列出的目录信息；表现层不直接枚举文件系统。</summary>
    public void SetFolders(IReadOnlyList<FolderItem> folders) => Folders = folders;
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
