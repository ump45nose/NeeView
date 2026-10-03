using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>独立面板结构、焦点和对话框适配；所有文件读取/编辑交给原业务层。</summary>
public sealed partial class PlaylistView : UserControl, IDisposable
{
    private PlaylistViewModel? _model;
    private bool _refreshing, _disposed;
    private bool _closing;
    private readonly HashSet<Task> _actions = [];
    private PlaylistRow? _pressed;
    private ListBox List => this.FindControl<ListBox>("PlaylistItems")!;
    public event EventHandler<string>? Failed;
    /// <summary>装载正式面板，隧道命中与列表多选交互保持原开书方式。</summary>
    public PlaylistView()
    {
        AvaloniaXamlLoader.Load(this);
        List.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Bubble, handledEventsToo: true);
        // 列表容器会先处理 Enter；在本作用域隧道消费，不能落入全局翻页绑定。
        List.AddHandler(KeyDownEvent, Item_KeyDown, RoutingStrategies.Tunnel);
    }
    /// <summary>唯一宿主传入既有业务实例，不创建第二套状态或后端。</summary>
    public void Attach(NeeView.BookOperation operation)
    {
        if (_model is not null) { _model.Refreshed -= Refresh; _model.Dispose(); } _model = new(operation); DataContext = _model;
        _model.Refreshed += Refresh; _ = RunAsync(() => _model.AttachAsync());
    }
    /// <summary>集合更新恢复仍存在的选中批次，屏蔽组合框加载时回写切换。</summary>
    private void Refresh(object? sender, EventArgs e)
    {
        if (_disposed || _model is null) return;
        _refreshing = true;
        bool hadFocus = List.IsKeyboardFocusWithin;
        try
        {
            var selected = List.SelectedItems?.OfType<PlaylistRow>().Select(row => row.Item).ToHashSet() ?? [];
            // 原前后登记命令更新 Hub 当前项；优先回显新当前项，批次仍包含它时才保留多选。
            if (_model.Hub.SelectedItem is { } current && !selected.Contains(current)) selected = [current];
            this.FindControl<ComboBox>("PlaylistFiles")!.ItemsSource = _model.Files;
            this.FindControl<ComboBox>("PlaylistFiles")!.SelectedItem = _model.CurrentFile;
            List.ItemsSource = _model.Rows;
            List.SelectedItems?.Clear();
            foreach (var row in _model.Rows.Where(row => selected.Contains(row.Item))) List.SelectedItems?.Add(row);
            if (List.SelectedItems?.Count == 0) List.SelectedItem = _model.Rows.FirstOrDefault(row => ReferenceEquals(row.Item, _model.Hub.SelectedItem));
        }
        finally { _refreshing = false; UpdateSelectionButtons(); if (hadFocus) FocusSelection(); }
    }
    /// <summary>集合刷新后恢复选中容器焦点，使方向键从实际当前行继续导航。</summary>
    private void FocusSelection()
    {
        if (_disposed || _closing) return;
        if (List.SelectedItem is { } selected) List.ScrollIntoView(selected);
        List.UpdateLayout();
        if (List.SelectedItem is { } item && List.ContainerFromItem(item) is Control row) row.Focus();
        else List.Focus();
    }
    /// <summary>实际选择文件由 Hub 校验，失败保留当前源并回显错误。</summary>
    private async void File_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _model is null || sender is not ComboBox { SelectedItem: PlaylistFileChoice file } || file.Path == _model.Hub.Current?.Path) return;
        await RunAsync(async () => { await _model.Hub.SwitchAsync(file.Path); await _model.Operation.SaveAsync(); });
    }
    /// <summary>列表选择不自动打开或解码图片；导航命令读取原当前选择。</summary>
    private void Item_Changed(object? sender, SelectionChangedEventArgs e)
    { if (!_refreshing && _model is not null) _model.Hub.SelectedItem = (List.SelectedItem as PlaylistRow)?.Item; UpdateSelectionButtons(); }
    /// <summary>仅有真实选择且来源可编辑时启用移除。</summary>
    private void UpdateSelectionButtons() => this.FindControl<Button>("PlaylistRemove")!.IsEnabled = _model?.CanEdit == true && List.SelectedItems?.Count > 0;
    /// <summary>包装可等待动作，失败由宿主展示；面板刷新不会加载正文。</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_disposed || _closing) return;
        Task? task = null;
        try { task = action(); _actions.Add(task); await task; } catch (Exception ex) { Failed?.Invoke(this, ex.Message); }
        finally { if (task is not null) _actions.Remove(task); if (!_disposed) _model?.Refresh(); }
    }
    /// <summary>登记共享选择首项，不隐式扩成当前双页帧。</summary>
    private async void Add_Click(object? sender, RoutedEventArgs e) { if (_model is not null) await RunAsync(_model.Operation.AddSelectedPlaylistPageAsync); }
    /// <summary>多选删除快照由原集合逆序恢复，不删图片。</summary>
    public async Task RemoveSelectedAsync()
    { if (_model is not null) { var items = List.SelectedItems?.OfType<PlaylistRow>().Select(row => row.Item).ToArray() ?? []; await RunAsync(() => _model.Hub.RemoveAsync(items)); } }
    /// <summary>按钮/上下文菜单共用同一批次删除入口。</summary>
    private async void Remove_Click(object? sender, RoutedEventArgs e) => await RemoveSelectedAsync();
    /// <summary>恢复原最近删除批次；保存失败保留恢复记录。</summary>
    private async void Restore_Click(object? sender, RoutedEventArgs e) { if (_model is not null) await RunAsync(() => _model.Hub.RestoreAsync()); }
    /// <summary>单项重排按原注册顺序目标移动；分组仅影响展示，不改变编辑对象。</summary>
    private async void Move_Click(object? sender, RoutedEventArgs e)
    {
        if (_model?.Hub.Current is not { } current || List.SelectedItem is not PlaylistRow row || sender is not Control { Tag: string step }) return;
        int index = current.Items.ToList().IndexOf(row.Item) + int.Parse(step);
        if (index >= 0 && index < current.Items.Count) await RunAsync(() => _model.Hub.MoveAsync(row.Item, current.Items[index]));
    }
    /// <summary>当前选择按原同书定位/跨书加载入口打开。</summary>
    public async Task OpenSelectedAsync() { if (_model is not null && List.SelectedItem is PlaylistRow row) { await RunAsync(() => _model.Operation.OpenPlaylistItemAsync(row.Item)); FocusSelection(); } }
    /// <summary>上下文菜单明确打开，不依赖单击配置。</summary>
    private async void Open_Click(object? sender, RoutedEventArgs e) => await OpenSelectedAsync();
    /// <summary>双击模式沿用原 Panels 配置。</summary>
    private async void Item_DoubleTapped(object? sender, TappedEventArgs e) { if (NeeView.Config.Current.Panels.OpenWithDoubleClick) await OpenSelectedAsync(); }
    /// <summary>Enter/Delete 在列表作用域消费，全局命令不能误触发翻页。</summary>
    private async void Item_KeyDown(object? sender, KeyEventArgs e)
    { if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Enter or Key.Delete)) return; e.Handled = true; if (e.Key == Key.Delete) await RemoveSelectedAsync(); else await OpenSelectedAsync(); }
    /// <summary>右击所选成员保留多选；空白清除，左击记录释放目标。</summary>
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        var row = RowAt(e.Source);
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None ? row : null;
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (row is null) List.SelectedItems?.Clear(); else if (List.SelectedItems?.Contains(row) != true) List.SelectedItem = row;
        e.Handled = true;
    }
    /// <summary>原无修饰单击释放打开，拖动或多选不打开。</summary>
    private async void Released(object? sender, PointerReleasedEventArgs e)
    {
        var row = _pressed; _pressed = null;
        if (!NeeView.Config.Current.Panels.OpenWithDoubleClick && e.InitialPressMouseButton == MouseButton.Left && e.KeyModifiers == KeyModifiers.None && row is not null && ReferenceEquals(row, RowAt(e.Source))) await OpenSelectedAsync();
    }
    /// <summary>从实际容器取得行，空白没有业务目标。</summary>
    private static PlaylistRow? RowAt(object? source) => source is Visual visual ? visual.GetVisualAncestors().Prepend(visual).OfType<ListBoxItem>().FirstOrDefault()?.DataContext as PlaylistRow : null;
    /// <summary>菜单按单项/多项/恢复记录显示真实能力。</summary>
    private void Context_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return; var items = menu.Items.OfType<MenuItem>().ToArray();
        items[0].IsEnabled = List.SelectedItem is PlaylistRow; items[1].IsEnabled = _model?.CanEdit == true && List.SelectedItems?.Count == 1;
        items[2].IsEnabled = _model?.CanEdit == true && List.SelectedItems?.Count > 0; items[3].IsEnabled = _model?.CanRestore == true;
    }
    /// <summary>更名只改变原登记别名，文件路径与图片不变。</summary>
    private async void Rename_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && List.SelectedItem is PlaylistRow row && await AskNameAsync("更改登记名称", row.Name) is { } name && !_closing && !_disposed) await RunAsync(() => _model.Hub.RenameAsync(row.Item, name)); }
    /// <summary>原更多菜单完整保留，未迁能力用禁用占位，避免静默删入口。</summary>
    public ContextMenu CreateMoreMenu()
    {
        var menu = new ContextMenu(); if (_model is null) return menu;
        foreach (var name in new[] { "普通列表", "详细内容", "横幅", "缩略图" }) menu.Items.Add(new MenuItem { Header = name + " · 尚未迁移", IsEnabled = false });
        menu.Items.Add(new Separator());
        Toggle("按来源分组", _model.Hub.Config.IsGroupBy, value => _model.Hub.Config.IsGroupBy = value);
        Toggle("仅显示当前书籍", _model.Hub.Config.IsCurrentBookFilterEnabled, value => _model.Hub.Config.IsCurrentBookFilterEnabled = value);
        menu.Items.Add(new Separator());
        Action("新建播放列表…", NewAsync, false); Action("打开播放列表…", OpenFileAsync, false);
        foreach (var name in new[] { "删除播放列表文件", "更名播放列表文件", "移除无效登记" }) menu.Items.Add(new MenuItem { Header = name + " · 尚未迁移", IsEnabled = false });
        Action("按路径排序", () => _model.Hub.SortAsync());
        menu.Items.Add(new MenuItem { Header = "作为书籍打开 · 尚未迁移", IsEnabled = false }); return menu;
        // 开关只写原配置和刷新面板，不重扫来源、不解码正文。
        void Toggle(string name, bool current, System.Action<bool> set)
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = current };
            item.Click += async (_, _) => await RunAsync(async () => { set(!current); await _model.Operation.SaveAsync(); }); menu.Items.Add(item);
        }
        void Action(string name, Func<Task> action, bool track = true)
        {
            var item = new MenuItem { Header = name };
            item.Click += async (_, _) => { if (_closing || _disposed) return; if (track) await RunAsync(action); else try { await action(); } catch (Exception ex) { Failed?.Invoke(this, ex.Message); } };
            menu.Items.Add(item);
        }
    }
    /// <summary>打开原更多菜单，不通过宿主改写列表布局。</summary>
    private void More_Click(object? sender, RoutedEventArgs e) { if (sender is Button button) { var menu = CreateMoreMenu(); button.ContextMenu = menu; menu.Open(button); } }
    /// <summary>系统选择器只回传路径，格式校验和读取在 Hub。</summary>
    private async Task OpenFileAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null || _model is null) return;
        var files = await storage.OpenFilePickerAsync(new() { Title = "打开 NeeView 播放列表", AllowMultiple = false, FileTypeFilter = [new("NeeView 播放列表") { Patterns = ["*.nvpls"] }] });
        if (!_closing && !_disposed && files.FirstOrDefault()?.TryGetLocalPath() is { } path) await RunAsync(async () => { await _model.Hub.SwitchAsync(path); await _model.Operation.SaveAsync(); });
    }
    /// <summary>新列表在原配置目录创建，禁止名称穿越或覆盖现有文件。</summary>
    private async Task NewAsync()
    {
        if (_model is null || await AskNameAsync("新建播放列表", "新播放列表") is not { } name) return;
        if (_closing || _disposed) return;
        await RunAsync(async () => { await _model.Hub.CreateNamedAsync(name); await _model.Operation.SaveAsync(); });
    }
    /// <summary>独立表现对话框只采集文字，不读写业务文件。</summary>
    private async Task<string?> AskNameAsync(string title, string text)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;
        var input = new TextBox { Text = text }; var ok = new Button { Content = "确定", IsDefault = true }; var cancel = new Button { Content = "取消", IsCancel = true };
        var dialog = new Window { Title = title, Width = 380, Height = 150, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { input, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } } } } };
        ok.Click += (_, _) => dialog.Close(input.Text ?? ""); cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); }; return await dialog.ShowDialog<string?>(owner);
    }
    /// <summary>关闭独立面板订阅，已授权写入由业务退出等待完成。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; if (_model is not null) { _model.Refreshed -= Refresh; _model.Dispose(); } }
    /// <summary>阻止新编辑，关闭已有输入对话框并等候授权动作真正完成。</summary>
    public async Task PrepareCloseAsync()
    {
        _closing = true; IsEnabled = false;
        if (TopLevel.GetTopLevel(this) is Window owner) foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
        foreach (var task in _actions.ToArray()) { try { await task; } catch { /* RunAsync 已报告动作失败。 */ } }
    }
    /// <summary>状态保存失败后允许重试，不能让仍运行窗口留下禁用面板。</summary>
    public void CancelClose() { if (!_disposed) { _closing = false; IsEnabled = true; } }
}
