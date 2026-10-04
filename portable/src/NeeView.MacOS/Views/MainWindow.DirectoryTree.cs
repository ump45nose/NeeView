// Copyright (c) NeeLaboratory. 原FolderTreeLayout/Bookshelf字段及面板分隔的Mac表现适配。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Task _folderTreeSettingsTask = Task.CompletedTask;
    private bool _folderTreeSettingsBusy;
    /// <summary>接入书架唯一树，后端只在节点展开/明确同步时执行。</summary>
    private void AttachDirectoryTree()
    {
        if (_model is null) return;
        this.FindControl<FolderTreeView>("BookshelfDirectoryTree")!.Attach(_model.Operation.Bookshelf.FolderTree);
        _model.Operation.Bookshelf.Changed += FolderTree_PlaceChanged;
        RefreshFolderTreeLayout();
    }
    /// <summary>仅更新书架内部表现布局，不触发阅读刷新或重复来源枚举。</summary>
    private void FolderTree_PlaceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        // 普通选择/枚举回报不重建GridLength，保留尚未完成的分隔条拖动。
        bool visible = Config.Current.Bookshelf.IsFolderTreeVisible && _model?.Operation.Bookshelf.IsBookmarkPlace == false;
        if (!_preparing && !_closedPrepared && this.FindControl<FolderTreeView>("BookshelfDirectoryTree")!.IsVisible != visible) RefreshFolderTreeLayout();
    });
    /// <summary>原Top/Left结构及隐藏状态；分隔尺寸从原Bookshelf配置读取。</summary>
    private void RefreshFolderTreeLayout()
    {
        var tree = this.FindControl<FolderTreeView>("BookshelfDirectoryTree")!;
        var body = this.FindControl<Grid>("FolderBrowserBody")!; var list = this.FindControl<DockPanel>("BookshelfItems")!;
        var splitter = this.FindControl<GridSplitter>("FolderTreeSplitter")!; var c = Config.Current.Bookshelf;
        bool visible = c.IsFolderTreeVisible && _model?.Operation.Bookshelf.IsBookmarkPlace == false;
        tree.IsVisible = splitter.IsVisible = visible;
        bool top = c.FolderTreeLayout == FolderTreeLayout.Top;
        body.RowDefinitions = top ? new() { new(new GridLength(visible ? c.FolderTreeAreaHeight : 0)), new(new GridLength(visible ? 4 : 0)), new(1, GridUnitType.Star) } : new() { new(1, GridUnitType.Star) };
        body.ColumnDefinitions = top ? new() { new(1, GridUnitType.Star) } : new() { new(new GridLength(visible ? c.FolderTreeAreaWidth : 0)), new(new GridLength(visible ? 4 : 0)), new(1, GridUnitType.Star) };
        Grid.SetRow(tree, 0); Grid.SetColumn(tree, 0);
        Grid.SetRow(splitter, top ? 1 : 0); Grid.SetColumn(splitter, top ? 0 : 1);
        Grid.SetRow(list, top ? 2 : 0); Grid.SetColumn(list, top ? 0 : 2);
        splitter.Width = top ? double.NaN : 4; splitter.Height = top ? 4 : double.NaN;
        splitter.ResizeDirection = top ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        splitter.HorizontalAlignment = top ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        splitter.VerticalAlignment = top ? VerticalAlignment.Center : VerticalAlignment.Stretch;
    }
    /// <summary>保存原树显示字段；显示成功后只同步当前路径父链，不递归扫描兄弟目录。</summary>
    /// <param name="visible">原Bookshelf树显隐值。</param>
    /// <returns>设置提交和可选路径同步结束；保存失败原地恢复。</returns>
    public Task SetFolderTreeVisibleAsync(bool visible) => ChangeFolderTreeSettingsAsync(
        () => Config.Current.Bookshelf.IsFolderTreeVisible = visible, sync: visible);
    /// <summary>保存原Top/Left方向，主题与树节点业务保持独立。</summary>
    /// <param name="layout">原Top/Left枚举。</param>
    /// <returns>保存及表现更新结束；非法值忽略。</returns>
    public Task SetFolderTreeLayoutAsync(FolderTreeLayout layout) => Enum.IsDefined(layout)
        ? ChangeFolderTreeSettingsAsync(() => Config.Current.Bookshelf.FolderTreeLayout = layout) : Task.CompletedTask;
    /// <summary>共享设置提交/关闭等待；失败恢复原字段及当前分隔布局。</summary>
    private Task ChangeFolderTreeSettingsAsync(Action change, bool sync = false)
    {
        if (_model is null || _preparing || _closedPrepared || _folderTreeSettingsBusy) return Task.CompletedTask;
        return _folderTreeSettingsTask = RunAsync();
        async Task RunAsync()
        {
            _folderTreeSettingsBusy = true; var c = Config.Current.Bookshelf;
            bool saved = false;
            var before = (c.IsFolderTreeVisible, c.FolderTreeLayout, c.FolderTreeAreaWidth, c.FolderTreeAreaHeight, c.IsSyncFolderTree, c.IsSyncFolderTreeAuto);
            try
            {
                change(); await _model.Operation.SaveConfigurationAsync(); RefreshFolderTreeLayout();
                MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
                saved = true;
            }
            catch (Exception ex)
            {
                (c.IsFolderTreeVisible, c.FolderTreeLayout, c.FolderTreeAreaWidth, c.FolderTreeAreaHeight, c.IsSyncFolderTree, c.IsSyncFolderTreeAuto) = before;
                RefreshFolderTreeLayout(); MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck); ShowError(ex.Message);
            }
            finally { _folderTreeSettingsBusy = false; }
            // 设置保存和来源读取是两个结果；目录不可访问不能撤销已经落盘的显隐设置。
            if (saved && sync) await _model.Operation.Bookshelf.FolderTree.SyncDirectoryAsync(_model.Operation.Bookshelf.Place, true);
        }
    }
    /// <summary>用户分隔条拖动只保存此树尺寸，不改变书架顺序或正文。</summary>
    private async void FolderTree_DragCompleted(object? sender, VectorEventArgs e)
    {
        var tree = this.FindControl<FolderTreeView>("BookshelfDirectoryTree")!; if (!tree.IsVisible) return;
        double width = tree.Bounds.Width, height = tree.Bounds.Height;
        await ChangeFolderTreeSettingsAsync(() =>
        { if (Config.Current.Bookshelf.FolderTreeLayout == FolderTreeLayout.Top) Config.Current.Bookshelf.FolderTreeAreaHeight = height; else Config.Current.Bookshelf.FolderTreeAreaWidth = width; });
    }
    /// <summary>书架原树显隐/方向/同步设置入口，各字段写回同一Bookshelf JSON。</summary>
    private void AddFolderTreeMenu(ContextMenu menu)
    {
        var c = Config.Current.Bookshelf;
        var visible = new MenuItem { Header = "目录树", ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsFolderTreeVisible };
        visible.Click += async (_, _) => await SetFolderTreeVisibleAsync(!c.IsFolderTreeVisible); menu.Items.Add(new Separator()); menu.Items.Add(visible);
        foreach (var layout in Enum.GetValues<FolderTreeLayout>())
        {
            var item = new MenuItem { Header = layout == FolderTreeLayout.Top ? "目录树在上方" : "目录树在左侧", ToggleType = MenuItemToggleType.Radio, IsChecked = c.FolderTreeLayout == layout };
            item.Click += async (_, _) => await SetFolderTreeLayoutAsync(layout); menu.Items.Add(item);
        }
        var manual = new MenuItem { Header = "同步按钮同步目录树", ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsSyncFolderTree };
        manual.Click += async (_, _) => await ChangeFolderTreeSettingsAsync(() => c.IsSyncFolderTree = !c.IsSyncFolderTree); menu.Items.Add(manual);
        var auto = new MenuItem { Header = "目录树自动同步", ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsSyncFolderTreeAuto };
        auto.Click += async (_, _) => await ChangeFolderTreeSettingsAsync(() => c.IsSyncFolderTreeAuto = !c.IsSyncFolderTreeAuto, true); menu.Items.Add(auto);
    }
}
