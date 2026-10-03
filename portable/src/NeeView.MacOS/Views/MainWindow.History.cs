using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;

namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private HistoryRow? _historyClickRow;
    /// <summary>Enter确认历史查询，数字/Delete/Backspace仍属于文本作用域。</summary>
    private async void HistorySearch_KeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; await SearchHistoryAsync(); } }
    /// <summary>原失焦确认语义；正常关闭不登记未确认的草稿。</summary>
    private async void HistorySearch_LostFocus(object? sender, RoutedEventArgs e) => await SearchHistoryAsync();
    /// <summary>统一可等待搜索入口；错误在独立模型展示，不打开当前选中项。</summary>
    /// <param name="recordHistory">Enter/失焦确认true，后台重筛false。</param>
    public async Task SearchHistoryAsync(bool recordHistory = true)
    { if (_model is not null && !_preparing && !_closedPrepared) await _model.HistorySearch.SearchAsync(recordHistory); }
    /// <summary>清空输入，增量关闭时沿原规则等待确认，不额外清空访问记录。</summary>
    private void HistorySearch_Clear(object? sender, RoutedEventArgs e)
    { if (_model is not null && !_preparing && !_closedPrepared) { _model.HistorySearch.Keyword = ""; this.FindControl<TextBox>("HistorySearchBox")!.Focus(); } }
    /// <summary>原表达式历史选择/单项删除；菜单仅消费表现集合，保存经原事务完成。</summary>
    private void HistorySearch_History(object? sender, RoutedEventArgs e)
    {
        if (_model is null || _preparing || _closedPrepared || sender is not Button button) return;
        var model = _model.HistorySearch; var menu = new ContextMenu();
        foreach (var keyword in model.History.ToArray())
        {
            var delete = new Button { Content = "×", Padding = new Thickness(4, 0) }; ToolTip.SetTip(delete, "删除搜索历史");
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6,
                Children = { new TextBlock { Text = keyword, MaxWidth = 190, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }, delete } };
            var item = new MenuItem { Header = row };
            item.Click += async (_, _) => { if (_preparing || _closedPrepared) return; model.Keyword = keyword; await SearchHistoryAsync(); };
            delete.Click += async (_, args) => { args.Handled = true; menu.Close(); await model.RemoveHistoryAsync(keyword); };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "无搜索历史", IsEnabled = false });
        button.ContextMenu = menu; menu.Open(button);
    }
    /// <summary>导航面板刷新只更新命令边界，不请求正文资源。</summary>
    private void History_Refreshed(object? sender, EventArgs e)
    { if (!_preparing && !_closedPrepared) RefreshHistoryCommandStates(); }

    /// <summary>原历史列表前后命令按当前过滤结果禁用；其余能力维持原迁移状态。</summary>
    private void RefreshHistoryCommandStates() => MenuPresenter.RefreshAvailability(this.FindControl<Menu>("MenuBar")!, name =>
        IsCommandAvailable(name) && (name switch
        {
            "PrevHistory" => _model?.Operation.IsLoading == false && _model.Operation.HistoryList.GetTarget(-1) is not null,
            "NextHistory" => _model?.Operation.IsLoading == false && _model.Operation.HistoryList.GetTarget(1) is not null,
            "ClearHistory" => _model?.Operation.IsLoading == false,
            "TogglePlaylistItem" => _model?.Operation.IsLoading == false && _model.Operation.Book?.CurrentPage is not null && _model.Operation.Playlists.Current is not null,
            "PrevPlaylistItemInBook" => _model?.Operation.CanMovePlaylistItemInBook(-1) == true,
            "NextPlaylistItemInBook" => _model?.Operation.CanMovePlaylistItemInBook(1) == true,
            "PrevPlaylistItem" or "NextPlaylistItem" => _model?.Operation.IsLoading == false && _model.Operation.Playlists.Current is not null,
            _ => true
        }));

    /// <summary>历史打开使用原 KeepHistoryOrder/SkipSamePlace，视图不读取来源。</summary>
    public async Task OpenHistoryAsync(string path)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        try { await _model.Operation.OpenHistoryAsync(path); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>打开当前选择；上下文菜单和 Enter 共用同一动作。</summary>
    private async void History_Open(object? sender, RoutedEventArgs e)
    { if (this.FindControl<ListBox>("HistoryList")!.SelectedItem is HistoryRow row) await OpenHistoryAsync(row.Path); }

    /// <summary>只移除真实选中批次；失败保留列表和选择，不删文件或书签。</summary>
    private async void History_Remove(object? sender, RoutedEventArgs e) => await RemoveSelectedHistoryAsync();

    /// <summary>读取控件的多选路径快照并交由 SaveData 提交，避免保存回报改变操作对象。</summary>
    public async Task RemoveSelectedHistoryAsync()
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var list = this.FindControl<ListBox>("HistoryList")!;
        var paths = list.SelectedItems?.OfType<HistoryRow>().Select(row => row.Path).ToArray() ?? [];
        try { await _model.SaveData.RemoveHistoryAsync(paths); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>列表 Enter/Delete 优先于全局绑定；文本框和其他输入作用域不参与。</summary>
    private async void History_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Enter or Key.Delete)) return;
        e.Handled = true;
        if (e.Key == Key.Delete) await RemoveSelectedHistoryAsync();
        else if (this.FindControl<ListBox>("HistoryList")!.SelectedItem is HistoryRow row) await OpenHistoryAsync(row.Path);
    }

    /// <summary>右击命中行才保留/改变选择；空白不沿用旧批次。</summary>
    private void History_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list) return;
        var row = HistoryRowFromSource(e.Source);
        _historyClickRow = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None ? row : null;
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (row is null) list.SelectedItems?.Clear();
        else if (list.SelectedItems?.Contains(row) != true) list.SelectedItem = row;
        e.Handled = true; // 防止 ListBox 随后把已有多选批次折叠成右击的单行。
    }

    /// <summary>原无修饰单击在同一行释放时打开；拖到别行、组合多选及双击模式不打开。</summary>
    private async void History_Released(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _historyClickRow; _historyClickRow = null;
        if (!Config.Current.Panels.OpenWithDoubleClick && e.InitialPressMouseButton == MouseButton.Left && e.KeyModifiers == KeyModifiers.None &&
            pressed is not null && ReferenceEquals(pressed, HistoryRowFromSource(e.Source))) await OpenHistoryAsync(pressed.Path);
    }

    /// <summary>命中实际列表容器，空白或非 Visual 事件源没有条目目标。</summary>
    private static HistoryRow? HistoryRowFromSource(object? source) => source is Visual visual
        ? visual.GetVisualAncestors().Prepend(visual).OfType<ListBoxItem>().FirstOrDefault()?.DataContext as HistoryRow : null;

    /// <summary>上下文菜单按实际选择启用；多选移除不简化为单项。</summary>
    private void History_ContextOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var list = this.FindControl<ListBox>("HistoryList")!;
        if (sender is ContextMenu menu)
        {
            var items = menu.Items.OfType<MenuItem>().ToArray();
            items[0].IsEnabled = list.SelectedItem is HistoryRow;
            items[1].IsEnabled = list.SelectedItems?.Count > 0;
        }
    }

    /// <summary>转换原更多菜单；未迁入的显示样式与无效清理保留禁用入口。</summary>
    private void History_More(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _model is null) return;
        var menu = new ContextMenu();
        foreach (var label in new[] { "普通列表", "详细内容", "横幅", "缩略图" })
            menu.Items.Add(new MenuItem { Header = label + " · 尚未迁移", IsEnabled = false });
        menu.Items.Add(new Separator());
        AddToggle("按日期分组", Config.Current.History.IsGroupBy, value => Config.Current.History.IsGroupBy = value);
        AddToggle("仅显示当前书籍所在目录", Config.Current.History.IsCurrentFolder, value => Config.Current.History.IsCurrentFolder = value);
        menu.Items.Add(new Separator());
        AddToggle("显示项目数", Config.Current.History.IsVisibleItemsCount, value => Config.Current.History.IsVisibleItemsCount = value);
        AddToggle("显示搜索框", Config.Current.History.IsVisibleSearchBox, value => Config.Current.History.IsVisibleSearchBox = value);
        AddToggle("逐次搜索", Config.Current.System.IsIncrementalSearchEnabled, value => Config.Current.System.IsIncrementalSearchEnabled = value);
        AddToggle("保存搜索历史", Config.Current.History.IsKeepSearchHistory, value => Config.Current.History.IsKeepSearchHistory = value);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "移除无效历史记录 · 尚未迁移", IsEnabled = false });
        var clear = new MenuItem { Header = "清空全部历史记录…", IsEnabled = _model.SaveData.HistoryEntries.Count > 0 };
        clear.Click += async (_, _) =>
        {
            if (!await ConfirmAsync("清空历史记录", "清空全部历史记录？此操作不删除文件和书签。", "清空") || _preparing || _closedPrepared) return;
            try { await _model.SaveData.ClearHistoryAsync(); } catch (Exception ex) { ShowError(ex.Message); }
        };
        menu.Items.Add(clear); button.ContextMenu = menu; menu.Open(button);

        // 表现开关写回原 Config.History，仅调整面板，不进入阅读解码链。
        void AddToggle(string label, bool current, Action<bool> set)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = current };
            item.Click += async (_, _) =>
            {
                if (_preparing || _closedPrepared) return;
                set(!current); _model.RefreshHistory();
                try { await _model.Operation.SaveAsync(); }
                catch (Exception ex) { set(current); _model.RefreshHistory(); ShowError(ex.Message); }
            };
            menu.Items.Add(item);
        }
    }
}
