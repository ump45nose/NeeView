using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>独立导航视图；控件组合和模板可调整，数据和交互通过表现模型接入。</summary>
public sealed class ReaderNavigationPanel : UserControl
{
    private readonly ListBox _pages = new();
    private readonly TreeView _tree = new();
    private readonly ListBox _history = new();
    private readonly TreeView _bookmarks = new();
    private readonly TextBlock _information = new() { Classes = { "reader-information" }, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sort = new() { ItemsSource = ReaderLabels.Choices(Enum.GetValues<SortMode>()) };
    private bool _updating;
    private string? _rootPath;

    /// <summary>组合导航面板；不接收窗口、具体存储或目录读取实现。</summary>
    public ReaderNavigationPanel(ReaderWorkspaceViewModel workspace, IReaderSession session, IImageRequestScheduler scheduler)
    {
        Classes.Add("reader-navigation");
        var tabs = new TabControl();
        _pages.ItemTemplate = new FuncDataTemplate<PageDescriptor>((page, _) =>
        {
            if (page is null) return new TextBlock();
            var row = new StackPanel { Classes = { "reader-page-row" }, Orientation = Orientation.Horizontal };
            row.Children.Add(new ThumbnailView(page, session, scheduler));
            row.Children.Add(new TextBlock { Classes = { "reader-page-name" }, Text = page.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }); return row;
        });
        _pages.SelectionChanged += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (!_updating && _pages.SelectedItem is PageDescriptor page) await session.LocateAsync(new(page.Id), true); }, workspace);
        _sort.SelectionChanged += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (!_updating && _sort.SelectedItem is ReaderChoice<SortMode> mode) await session.SetOptionsAsync(session.Snapshot.Options with { Sort = mode.Value }); }, workspace);
        var pages = new DockPanel(); DockPanel.SetDock(_sort, Dock.Top); pages.Children.Add(_sort); pages.Children.Add(_pages);
        _tree.ItemTemplate = new FuncTreeDataTemplate<FolderNode>((node, _) => new TextBlock { Text = node?.ToString() }, node => node.Children);
        _tree.SelectionChanged += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (_updating || _tree.SelectedItem is not FolderNode node) return; await workspace.ExpandFolderAsync(node); await workspace.OpenAsync(node.Path); }, workspace);
        _history.ItemTemplate = new FuncDataTemplate<ReadingState>((state, _) => new TextBlock { Classes = { "reader-list-label" }, Text = state?.Locator.Path, TextTrimming = TextTrimming.CharacterEllipsis });
        _history.DoubleTapped += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (_history.SelectedItem is ReadingState state) await workspace.OpenAsync(state.Locator.Path); }, workspace);
        _bookmarks.ItemTemplate = new FuncTreeDataTemplate<BookmarkNode>((node, _) => new TextBlock { Classes = { "reader-list-label" }, Text = node?.Item.Name }, node => node.Children);
        _bookmarks.DoubleTapped += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (_bookmarks.SelectedItem is BookmarkNode node) await workspace.OpenBookmarkAsync(node.Item); }, workspace);
        var marks = new DockPanel(); var actions = new WrapPanel();
        foreach (var (label, action) in new[] { ("新建文件夹", "folder"), ("重命名", "rename"), ("删除", "delete") })
            actions.Children.Add(ReaderPanelControls.Button(label, () => workspace.EditBookmarkAsync((_bookmarks.SelectedItem as BookmarkNode)?.Item, action), workspace));
        DockPanel.SetDock(actions, Dock.Bottom); marks.Children.Add(actions); marks.Children.Add(_bookmarks);
        tabs.ItemsSource = new[] { new TabItem { Header = "页面", Content = pages }, new TabItem { Header = "目录", Content = _tree }, new TabItem { Header = "历史", Content = _history }, new TabItem { Header = "书签", Content = marks }, new TabItem { Header = "信息", Content = new ScrollViewer { Content = _information } } };
        Content = tabs;
    }
    /// <summary>只更新阅读表现值，守卫程序选择事件不重复触发导航。</summary>
    public void SetSnapshot(ReaderSnapshot snapshot)
    {
        _updating = true;
        try
        {
            if (!ReferenceEquals(_pages.ItemsSource, snapshot.Index?.Pages)) _pages.ItemsSource = snapshot.Index?.Pages;
            _pages.SelectedItem = snapshot.Current;
            _sort.SelectedItem = new ReaderChoice<SortMode>(snapshot.Options.Sort, ReaderLabels.Text(snapshot.Options.Sort));
            _information.Text = snapshot.Current is { } page ? $"{page.Name}\n\n来源：{snapshot.Index?.Locator.Path}\n尺寸：{page.Size?.Width} × {page.Size?.Height}\n字节：{page.Version.Length:N0}\n方向：{ReaderLabels.Text(snapshot.Options.Direction)}\n模式：{ReaderLabels.Text(snapshot.Options.Mode)}\n来源访问：{snapshot.Index?.Capabilities.AccessCost}" : "当前没有图片。";
        }
        finally { _updating = false; }
    }
    /// <summary>应用独立面板数据；同目录刷新保留已展开树节点。</summary>
    public void SetData(NavigationData data)
    {
        _updating = true;
        try
        {
            _history.ItemsSource = data.History; _bookmarks.ItemsSource = data.Bookmarks;
            if (_rootPath != data.Root?.Path) { _rootPath = data.Root?.Path; _tree.ItemsSource = data.Root is { } root ? new[] { root } : []; }
        }
        finally { _updating = false; }
    }
}
