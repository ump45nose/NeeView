using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>仅处理书签列表表现和输入；打开、编辑、持久化通过宿主契约，视图不读写文件。</summary>
public sealed partial class BookmarkListView : UserControl, IDisposable
{
    private BookmarkListViewModel? _model;
    private bool _refreshing, _disposed, _busy;
    private bool _closing;
    private readonly HashSet<Task> _actions = [];
    private BookmarkNode? _pressed;
    private Point _pressedPoint;
    private ListBox List => this.FindControl<ListBox>("BookmarkItems")!;
    /// <summary>普通视图插槽：导航栏始终在上，可选编辑树由宿主提供，控件不依赖树业务模型。</summary>
    public Control? TreeContent
    {
        get => this.FindControl<ContentControl>("BookmarkTreeHost")!.Content as Control;
        set => this.FindControl<ContentControl>("BookmarkTreeHost")!.Content = value;
    }
    public BookmarkFolderList? Navigation => _model?.List;
    public IReadOnlyList<BookmarkNode> SelectedNodes => List.SelectedItems?.OfType<BookmarkNode>().ToArray() ?? [];
    public Func<BookmarkNode, Task>? OpenBookAsync { get; set; }
    public Func<Task>? SaveSettingsAsync { get; set; }
    public Func<string?>? CurrentBookPath { get; set; }
    public event EventHandler? SelectionUpdated;
    public event EventHandler? TreeVisibilityUpdated;
    public event EventHandler<string>? Failed;

    /// <summary>装载正式 XAML；列表拥有自己的 Enter/返回/方向键，不修改全局阅读键位。</summary>
    public BookmarkListView()
    {
        AvaloniaXamlLoader.Load(this);
        List.AddHandler(KeyDownEvent, Item_KeyDown, RoutingStrategies.Tunnel);
        List.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Bubble, handledEventsToo: true);
    }
    /// <summary>接入已有 SaveData；再次装配先退订旧表现模型。</summary>
    public void Attach(SaveData state)
    {
        if (_model is not null) { _model.Refreshed -= Refresh; _model.Dispose(); }
        _model = new(state); DataContext = _model; _model.Refreshed += Refresh; Refresh(this, EventArgs.Empty);
    }
    /// <summary>刷新列表并保持存活的选择批次；排序和集合事务不改变正文。</summary>
    private void Refresh(object? sender, EventArgs e)
    {
        if (_disposed || _model is null) return;
        var selection = SelectedNodes.ToHashSet();
        if (_model.List.SelectedItem is { } target && !selection.Contains(target)) selection = [target];
        _refreshing = true;
        try
        {
            List.ItemsSource = _model.Items; List.SelectedItems?.Clear();
            foreach (var node in _model.Items.Where(selection.Contains)) List.SelectedItems?.Add(node);
            if (List.SelectedItems?.Count == 0) List.SelectedItem = _model.List.SelectedItem;
        }
        finally { _refreshing = false; }
        SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>实际选中项回到导航业务；选择本身不加载书籍。</summary>
    private void Item_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _model is null) return;
        _model.List.Select(List.SelectedItem as BookmarkNode); SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>成功编辑后的原节点定位，不把数组下标作为当前位置。</summary>
    public void Reveal(BookmarkNode? node)
    {
        if (_disposed || _model is null) return;
        if (node is not null) _model.List.Reveal(node); else _model.List.Select(null);
        _refreshing = true;
        try { List.SelectedItems?.Clear(); } finally { _refreshing = false; }
        _model.Refresh();
    }
    /// <summary>保存失败后恢复当前目录中的同一批次，保留重试对象。</summary>
    public void RestoreSelection(IEnumerable<BookmarkNode> nodes)
    {
        if (_disposed || _model is null) return;
        _refreshing = true;
        try { List.SelectedItems?.Clear(); foreach (var node in nodes.Where(_model.Items.Contains)) List.SelectedItems?.Add(node); }
        finally { _refreshing = false; }
        _model.List.Select(List.SelectedItem as BookmarkNode); SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>可选树选择文件夹时显示其内容，选择书籍时显示其父级；继续复用原节点。</summary>
    public void SyncTreeSelection(BookmarkNode? node)
    {
        if (_disposed || _closing || _model is null || node is null) return;
        if (node.IsFolder) _model.List.SetPlace(node); else _model.List.Reveal(node);
        _model.Refresh();
    }
    /// <summary>串行面板动作并跟踪实际任务；关闭等待已开始的动作，不接收新的输入。</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_disposed || _closing || _busy) return;
        _busy = true; Task? task = null;
        try { task = action(); _actions.Add(task); await task; }
        catch (Exception ex) { if (!_disposed) Failed?.Invoke(this, ex.Message); }
        finally { if (task is not null) _actions.Remove(task); _busy = false; }
    }
    /// <summary>统一可等待打开入口；文件夹只改变书签位置，书籍交给现有阅读链。</summary>
    public async Task OpenSelectedAsync()
    {
        if (_model is null || List.SelectedItem is not BookmarkNode node) return;
        await RunAsync(async () =>
        {
            if (node.IsFolder) { _model.List.SetPlace(node); _model.Refresh(); }
            else if (OpenBookAsync is { } open) await open(node);
            if (!_closing) FocusSelection();
        });
    }
    /// <summary>导航后将键盘焦点置于实际所选容器，不影响其他应用的系统焦点。</summary>
    private void FocusSelection()
    {
        if (_disposed || _closing) return;
        if (List.SelectedItem is { } node) List.ScrollIntoView(node);
        List.UpdateLayout();
        if (List.SelectedItem is { } selected && List.ContainerFromItem(selected) is Control row) row.Focus(); else List.Focus();
    }
    /// <summary>回到根位置仅更新列表；不打开磁盘目录。</summary>
    private void Root_Click(object? sender, RoutedEventArgs e) { if (_model is not null && !_busy && !_closing) { _model.List.MoveToRoot(); _model.Refresh(); FocusSelection(); } }
    /// <summary>原返回后定位刚离开的文件夹。</summary>
    private void Up_Click(object? sender, RoutedEventArgs e) { if (_model is not null && !_busy && !_closing) { _model.List.MoveToParent(); _model.Refresh(); FocusSelection(); } }
    /// <summary>同步当前书籍按原当前目录优先规则；未找到不离开当前位置。</summary>
    private void Sync_Click(object? sender, RoutedEventArgs e) { if (_model is not null && !_busy && !_closing) { _model.List.Sync(CurrentBookPath?.Invoke()); _model.Refresh(); FocusSelection(); } }
    /// <summary>改动排序后等待既有保存；失败恢复原配置和值，允许原地重试。</summary>
    private async void Order_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _disposed || _model is null || sender is not ComboBox { SelectedItem: BookmarkOrderChoice choice } || choice.Mode == _model.List.FolderOrder) return;
        if (_busy || _closing) { _model.Refresh(); return; }
        if (!choice.IsEnabled) { _model.Refresh(); return; }
        await RunAsync(async () =>
        {
            var before = Config.Current.Bookmark.BookmarkFolderOrder;
            try { _model.List.ChangeOrder(choice.Mode); _model.Refresh(); if (SaveSettingsAsync is { } save) await save(); }
            catch { Config.Current.Bookmark.BookmarkFolderOrder = before; _model.Refresh(); throw; }
        });
    }
    /// <summary>保留未迁移的原能力占位，不能把简化名称包含搜索冒充原查询语法。</summary>
    private void More_Click(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var tree = new MenuItem { Header = "显示书签树（编辑 / 拖动）", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _model.TreeVisible };
        var count = new MenuItem { Header = "显示条目数量", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _model.CountVisible };
        tree.Click += async (_, _) => await SaveDisplayAsync(true); count.Click += async (_, _) => await SaveDisplayAsync(false);
        var menu = new ContextMenu { ItemsSource = new Control[] { tree, count, new Separator(),
            new MenuItem { Header = "搜索 / 递归搜索（待迁移）", IsEnabled = false },
            new MenuItem { Header = "Normal / Banner / Thumbnail 模板（待迁移）", IsEnabled = false },
            new MenuItem { Header = "同步书架选项 / 路径修复 / 移除无效（待迁移）", IsEnabled = false } } };
        menu.Open(this.FindControl<Button>("BookmarkMore")!);
    }
    /// <summary>仅保存原显示字段；失败恢复运行时值，树显隐通知宿主。</summary>
    private async Task SaveDisplayAsync(bool tree)
    {
        if (_model is null || _disposed) return;
        await RunAsync(async () =>
        {
            var before = tree ? Config.Current.Bookmark.IsFolderTreeVisible : Config.Current.Bookmark.IsVisibleItemsCount;
            try
            {
                if (tree) Config.Current.Bookmark.IsFolderTreeVisible = !before; else Config.Current.Bookmark.IsVisibleItemsCount = !before;
                _model.Refresh(); TreeVisibilityUpdated?.Invoke(this, EventArgs.Empty);
                if (SaveSettingsAsync is { } save) await save();
            }
            catch
            {
                if (tree) Config.Current.Bookmark.IsFolderTreeVisible = before; else Config.Current.Bookmark.IsVisibleItemsCount = before;
                _model.Refresh(); TreeVisibilityUpdated?.Invoke(this, EventArgs.Empty); throw;
            }
        });
    }
    /// <summary>菜单明确打开，不受单击/双击配置影响。</summary>
    private async void Open_Click(object? sender, RoutedEventArgs e) => await OpenSelectedAsync();
    /// <summary>双击模式按原规则打开主选中项（可属于多选批次）；空白不重开旧书。</summary>
    private async void Item_DoubleTapped(object? sender, TappedEventArgs e) { if (Config.Current.Panels.OpenWithDoubleClick && GetRow(e.Source)?.DataContext is BookmarkNode node && ReferenceEquals(List.SelectedItem, node)) await OpenSelectedAsync(); }
    /// <summary>列表专属 Enter 与 Backspace；其他方向键保留框架的列表选择语义。</summary>
    private async void Item_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter) { e.Handled = true; await OpenSelectedAsync(); }
        else if (e.Key == Key.Back) { e.Handled = true; Up_Click(this, new()); }
    }
    /// <summary>按实际容器解析节点，展开树和列表使用同一原节点身份。</summary>
    private static ListBoxItem? GetRow(object? source) => source is Visual visual ? visual as ListBoxItem ?? visual.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() : null;
    /// <summary>记录真实左键命中；右击已有成员保留批次，空白清除选择。</summary>
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        if (GetRow(e.Source) is null && e.GetCurrentPoint(List).Properties.IsRightButtonPressed) { List.SelectedItems?.Clear(); return; }
        if (e.GetCurrentPoint(List).Properties.IsRightButtonPressed && GetRow(e.Source)?.DataContext is BookmarkNode node && SelectedNodes.Contains(node)) { e.Handled = true; return; }
        if (e.GetCurrentPoint(List).Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None) { _pressed = GetRow(e.Source)?.DataContext as BookmarkNode; _pressedPoint = e.GetPosition(List); }
    }
    /// <summary>普通单击仅在同一行释放且无滚动/拖动时打开；多选不产生打开动作。</summary>
    private async void Released(object? sender, PointerReleasedEventArgs e)
    {
        var node = _pressed; _pressed = null;
        if (Config.Current.Panels.OpenWithDoubleClick || e.InitialPressMouseButton != MouseButton.Left || e.KeyModifiers != KeyModifiers.None || node is null || SelectedNodes.Count != 1) return;
        var delta = e.GetPosition(List) - _pressedPoint;
        if (!ReferenceEquals(GetRow(e.Source)?.DataContext, node) || !ReferenceEquals(List.SelectedItem, node) || Math.Abs(delta.X) + Math.Abs(delta.Y) > 6) return;
        await OpenSelectedAsync();
    }
    /// <summary>宿主关闭后解除数据订阅；未完成输入不得继续更新面板。</summary>
    public void Dispose() { _disposed = true; if (_model is not null) { _model.Refreshed -= Refresh; _model.Dispose(); } }
    /// <summary>关闭前等待已有打开/设置动作；失败已经由动作入口报告，最终状态保存由宿主负责。</summary>
    public async Task PrepareCloseAsync()
    {
        _closing = true;
        try { await Task.WhenAll(_actions.ToArray()); } catch { /* RunAsync 已展示错误并执行设置回滚。 */ }
    }
    /// <summary>宿主最终保存失败时重新允许输入，保留原面板状态以便重试。</summary>
    public void CancelClose() => _closing = false;
}
