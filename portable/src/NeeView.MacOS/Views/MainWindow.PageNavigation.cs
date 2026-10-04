// Copyright (c) NeeLaboratory. 原PageList目录/名称/搜索表现和宿主边界的Mac适配。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private Book? _contentsBook;
    private long _contentsVersion = -1;
    private CancellationTokenSource? _contentsRequest;
    private ContentsPageNode? _contentsRoot;
    private Task _pageNavigationSettings = Task.CompletedTask;
    private bool _pageNavigationBusy;
    private void AttachPageNavigation()
    {
        _model!.Refreshed += PageNavigation_Refreshed;
        _model.Operation.Bookshelf.Post = action => Dispatcher.UIThread.Post(action);
        RefreshPageTreeLayout(); PageNavigation_Refreshed(this, EventArgs.Empty);
    }
    /// <summary>组标题沿当前正文顺序及原Smart目录，保持Page作为条目身份。</summary>
    private string? GetPageGroupHeader(Page page)
    {
        var pages = _model?.Operation.Book?.Pages;
        if (!Config.Current.PageList.IsGroupBy || pages is null || !pages.Contains(page)) return null;
        var directory = page.GetSmartDirectoryDisplayString();
        return page.Index == 0 || pages[page.Index - 1].GetSmartDirectoryDisplayString() != directory ? directory.Length == 0 ? "根目录" : directory : null;
    }
    /// <summary>仅来源代次改变才后台建立目录树；翻页、搜索和反序不重新扫描来源。</summary>
    private async void PageNavigation_Refreshed(object? sender, EventArgs e)
    {
        if (_preparing || _closedPrepared || _model is null) return;
        var book = _model.Operation.Book;
        if (ReferenceEquals(book, _contentsBook) && (book?.Pages.SourceVersion ?? -1) == _contentsVersion) return;
        _contentsRequest?.Cancel(); var request = new CancellationTokenSource(); _contentsRequest = request;
        _contentsBook = book; _contentsVersion = book?.Pages.SourceVersion ?? -1; var published = false;
        try
        {
            var source = book?.Pages.SourcePages ?? [];
            var root = await Task.Run(() => BookTableOfContents.Create(source, request.Token), request.Token);
            if (request.IsCancellationRequested || _preparing || _closedPrepared || !ReferenceEquals(book, _model.Operation.Book)) return;
            _contentsRoot = root; this.FindControl<TreeView>("ContentsTree")!.ItemsSource = new[] { root }; published = true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) ShowError(ex.Message); }
        finally
        {
            if (ReferenceEquals(_contentsRequest, request))
            {
                _contentsRequest = null;
                // 未发布的版本不能成为缓存命中：构建失败或退出被取消后仍允许同源刷新重试。
                if (!published) { _contentsBook = null; _contentsVersion = long.MinValue; }
            }
            request.Dispose();
        }
    }
    private void RefreshPageTreeLayout()
    {
        var c = Config.Current.PageList; bool top = c.FolderTreeLayout == FolderTreeLayout.Top, visible = c.IsFolderTreeVisible;
        var body = this.FindControl<Grid>("PageBrowserBody")!; var tree = this.FindControl<TreeView>("ContentsTree")!;
        var splitter = this.FindControl<GridSplitter>("ContentsTreeSplitter")!; var list = this.FindControl<ListBox>("PageList")!;
        tree.IsVisible = splitter.IsVisible = visible;
        body.RowDefinitions = top ? new() { new(new GridLength(visible ? c.FolderTreeAreaHeight : 0)), new(new GridLength(visible ? 4 : 0)), new(1, GridUnitType.Star) } : new() { new(1, GridUnitType.Star) };
        body.ColumnDefinitions = top ? new() { new(1, GridUnitType.Star) } : new() { new(new GridLength(visible ? c.FolderTreeAreaWidth : 0)), new(new GridLength(visible ? 4 : 0)), new(1, GridUnitType.Star) };
        Grid.SetRow(tree, 0); Grid.SetColumn(tree, 0); Grid.SetRow(splitter, top ? 1 : 0); Grid.SetColumn(splitter, top ? 0 : 1);
        Grid.SetRow(list, top ? 2 : 0); Grid.SetColumn(list, top ? 0 : 2);
        splitter.Width = top ? double.NaN : 4; splitter.Height = top ? 4 : double.NaN; splitter.ResizeDirection = top ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        splitter.HorizontalAlignment = top ? HorizontalAlignment.Stretch : HorizontalAlignment.Center; splitter.VerticalAlignment = top ? VerticalAlignment.Center : VerticalAlignment.Stretch;
    }
    /// <summary>只保存PageList相关原字段，失败原地回滚；纯表现调整不修改阅读和排序。</summary>
    /// <param name="change">本次已确认的原配置修改。</param><returns>保存及必要重筛完成后的任务。</returns>
    public Task ChangePageNavigationAsync(Action<PageListConfig> change)
    {
        if (_model is null || _preparing || _closedPrepared || _pageNavigationBusy) return Task.CompletedTask;
        return _pageNavigationSettings = RunAsync();
        async Task RunAsync()
        {
            _pageNavigationBusy = true; var c = Config.Current.PageList;
            var old = (c.Format, c.ShowBookTitle, c.IsGroupBy, c.IsVisibleSearchBox, c.IsVisibleItemsCount, c.FocusMainView, c.IsFolderTreeVisible, c.FolderTreeLayout, c.FolderTreeAreaWidth, c.FolderTreeAreaHeight);
            try { change(c); await _model.Operation.SaveConfigurationAsync(); }
            catch (Exception ex) { (c.Format, c.ShowBookTitle, c.IsGroupBy, c.IsVisibleSearchBox, c.IsVisibleItemsCount, c.FocusMainView, c.IsFolderTreeVisible, c.FolderTreeLayout, c.FolderTreeAreaWidth, c.FolderTreeAreaHeight) = old; ShowError(ex.Message); }
            finally { _pageNavigationBusy = false; }
            RefreshPageTreeLayout(); _model.RefreshNavigationPanel(); _pagePresentation?.RefreshCovers();
            MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
            if (old.Format != c.Format && _model.Operation.Book is { } book && book.Pages.SearchKeyword.Length > 0)
                try { await _model.Operation.SearchPagesAsync(book.Pages.SearchKeyword, book); } catch (Exception ex) { ShowError(ex.Message); }
        }
    }
    private async void ContentsTree_DragCompleted(object? sender, VectorEventArgs e)
    { var tree = this.FindControl<TreeView>("ContentsTree")!; double width = tree.Bounds.Width, height = tree.Bounds.Height; await ChangePageNavigationAsync(c => { if (c.FolderTreeLayout == FolderTreeLayout.Top) c.FolderTreeAreaHeight = height; else c.FolderTreeAreaWidth = width; }); }
    private async void ContentsTree_KeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; await CommitContentsAsync(this.FindControl<TreeView>("ContentsTree")!.SelectedItem as ContentsPageNode); } }
    private async void ContentsTree_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual visual) return;
        var chain = visual.GetVisualAncestors().Prepend(visual).ToArray(); if (chain.Any(v => v is ToggleButton or ScrollBar)) return;
        if (chain.OfType<TreeViewItem>().FirstOrDefault()?.DataContext is ContentsPageNode node) { e.Handled = true; await CommitContentsAsync(node); }
    }
    /// <summary>目录只定位原代表页；当前过滤隐藏的页明确提示，不改变搜索条件。</summary>
    /// <param name="node">当前目录树节点。</param><returns>唯一正文定位任务。</returns>
    public async Task CommitContentsAsync(ContentsPageNode? node)
    {
        if (node?.Page is not { } page || _model?.Operation.Book is not { } book || !ReferenceEquals(_contentsBook, book)) return;
        if (!book.Pages.Contains(page)) { ShowError("此目录的代表页被当前搜索过滤，请清空页面搜索后跳转。"); return; }
        await CommitPageListAsync(page);
    }
    private void AddPageNavigationMenu(ContextMenu menu)
    {
        var c = Config.Current.PageList;
        menu.Items.Add(new Separator());
        foreach (var format in Enum.GetValues<PageNameFormat>())
        { var item = new MenuItem { Header = format switch { PageNameFormat.Smart => "智能名称", PageNameFormat.NameOnly => "仅文件名", PageNameFormat.Raw => "完整条目名", _ => "页码" }, ToggleType = MenuItemToggleType.Radio, IsChecked = c.Format == format }; item.Click += async (_, _) => await ChangePageNavigationAsync(p => p.Format = format); menu.Items.Add(item); }
        Toggle("显示书名", c.ShowBookTitle, p => p.ShowBookTitle = !p.ShowBookTitle);
        Toggle("按目录分组", c.IsGroupBy, p => p.IsGroupBy = !p.IsGroupBy);
        Toggle("显示搜索框", c.IsVisibleSearchBox, p => p.IsVisibleSearchBox = !p.IsVisibleSearchBox);
        Toggle("显示项目数", c.IsVisibleItemsCount, p => p.IsVisibleItemsCount = !p.IsVisibleItemsCount);
        Toggle("确认后焦点回正文", c.FocusMainView, p => p.FocusMainView = !p.FocusMainView);
        Toggle("目录组树", c.IsFolderTreeVisible, p => p.IsFolderTreeVisible = !p.IsFolderTreeVisible);
        foreach (var layout in Enum.GetValues<FolderTreeLayout>())
        { var item = new MenuItem { Header = layout == FolderTreeLayout.Top ? "目录组树在上方" : "目录组树在左侧", ToggleType = MenuItemToggleType.Radio, IsChecked = c.FolderTreeLayout == layout }; item.Click += async (_, _) => await ChangePageNavigationAsync(p => p.FolderTreeLayout = layout); menu.Items.Add(item); }
        void Toggle(string label, bool value, Action<PageListConfig> action)
        { var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = value }; item.Click += async (_, _) => await ChangePageNavigationAsync(action); menu.Items.Add(item); }
    }
    private NavigationSearchViewModel? SearchModel(object? sender) => sender is Control { Name: "PageListSearchBox" } or Control { Tag: "Page" } ? _model?.PageSearch : _model?.FolderSearch;
    private async void NavigationSearch_KeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; if (SearchModel(sender) is { } model && !_preparing && !_closedPrepared) await model.SearchAsync(); } }
    private async void NavigationSearch_LostFocus(object? sender, RoutedEventArgs e)
    { if (!_preparing && !_closedPrepared && SearchModel(sender) is { } model) await model.SearchAsync(); }
    private async void NavigationSearch_Clear(object? sender, RoutedEventArgs e)
    { if (!_preparing && !_closedPrepared && SearchModel(sender) is { } model) { model.Keyword = ""; await model.SearchAsync(false); } }
    private void NavigationSearch_History(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || SearchModel(sender) is not { } model || _preparing || _closedPrepared) return;
        var menu = new ContextMenu();
        foreach (var keyword in model.History.ToArray())
        {
            var delete = new Button { Content = "×", Padding = new Thickness(4, 0) }; var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = keyword, MaxWidth = 190 }, delete } };
            var item = new MenuItem { Header = row }; item.Click += async (_, _) => { if (!_preparing && !_closedPrepared) { model.Keyword = keyword; await model.SearchAsync(); } };
            delete.Click += async (_, args) => { args.Handled = true; menu.Close(); await model.RemoveHistoryAsync(keyword); }; menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "无搜索历史", IsEnabled = false }); button.ContextMenu = menu; menu.Open(button);
    }
}
