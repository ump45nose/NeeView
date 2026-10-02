using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>窗口表现状态；视图可独立调整，所有阅读动作进入原 BookOperation。</summary>
public sealed class ReaderWorkspaceViewModel(BookOperation operation, CommandTable commands, SaveData saveData)
    : ObservableObject
{
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
    public double PageIndex => Operation.Book?.CurrentPage?.Index ?? 0;
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
    public bool LeftVisible => Config.Current.Panels.IsLeftVisible && (!LeftAutoHide || _leftHovered);
    public bool RightVisible => Config.Current.Panels.IsRightVisible && (!RightAutoHide || _rightHovered);
    public bool LeftAutoHide => Config.Current.Panels.IsLeftAutoHide;
    public bool RightAutoHide => Config.Current.Panels.IsRightAutoHide;
    private bool _leftHovered, _rightHovered;
    private bool _pageList;
    public bool ShowPageList => _pageList;
    public bool ShowFolderList => !_pageList;
    public string LeftTitle => _pageList ? "页面列表" : "文件夹";
    public event EventHandler? Refreshed;
    public event EventHandler? PanelsRefreshed;

    /// <summary>装配业务订阅；后台回报统一切 UI 线程。</summary>
    public void Attach() { Operation.Changed += Operation_Changed; Refresh(); }
    /// <summary>关闭窗口时解除订阅，避免旧窗口收到新书变化。</summary>
    public void Detach() => Operation.Changed -= Operation_Changed;
    /// <summary>界面只展示最新业务状态，排队回报不会携带旧书快照。</summary>
    private void Operation_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);
    /// <summary>刷新绑定；页面对象保持原身份，不创建另一套页面模型。</summary>
    public void Refresh()
    {
        Address = Operation.Book?.Path ?? Address; SelectedPage = Operation.Book?.CurrentPage;
        OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>接收后端列出的目录信息；表现层不直接枚举文件系统。</summary>
    public void SetFolders(IReadOnlyList<FolderItem> folders) => Folders = folders;
    /// <summary>切换已支持侧栏页面或显隐，业务阅读位置不变。</summary>
    public void SelectPanel(string name)
    {
        if (name is "FolderPanel" or "PageListPanel")
        {
            bool pageList = name == "PageListPanel";
            Config.Current.Panels.IsLeftVisible = !Config.Current.Panels.IsLeftVisible || _pageList != pageList;
            _pageList = pageList;
        }
        else Config.Current.Panels.IsRightVisible = !Config.Current.Panels.IsRightVisible;
        RefreshPanels();
    }
    /// <summary>只通知侧栏绑定，不发布阅读刷新或重新申请图像。</summary>
    public void RefreshPanels()
    {
        foreach (var name in new[] { nameof(LeftVisible), nameof(RightVisible), nameof(LeftAutoHide), nameof(RightAutoHide), nameof(ShowPageList), nameof(ShowFolderList), nameof(LeftTitle) }) OnPropertyChanged(name);
        PanelsRefreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>自动隐藏只改变窗口表现状态，不改变书籍和目录索引。</summary>
    public void Hover(bool left, bool value) { if (left) _leftHovered = value; else _rightHovered = value; RefreshPanels(); }
}
