using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private bool _sliderDragging;
    private bool _sliderUpdating;
    private double _wheel;
    private double _leftWidth, _rightWidth;
    private SidePanelPresenter? _sidePanels;
    public ReaderView Viewer => this.FindControl<ReaderView>("MainViewSocket")!;
    private ThumbnailView FilmStrip => this.FindControl<ThumbnailView>("DockFilmStripSocket")!;
    private ThumbnailView NavigatorView => this.FindControl<ThumbnailView>("Navigator")!;
    private static readonly HashSet<string> HostCommands = new(StringComparer.Ordinal)
    {
        "LoadAs", "OpenFolder", "ReLoad", "ParentFolder", "OpenExplorer", "CloseWindow", "CloseApplication", "ToggleFullScreen",
        "ViewScaleUp", "ViewScaleDown", "SetStretchModeUniform", "SetStretchModeNone", "ToggleHideLeftPanel", "ToggleHideRightPanel",
        "LeftAutoHide", "RightAutoHide", "OpenOptionsWindow", "HelpCommandList", "ToggleBookmark", "LoadRecentBook", "OpenBookExplorer",
        "ToggleVisibleBookshelf", "ToggleVisiblePageList", "ToggleVisibleHistoryList", "ToggleVisibleFileInfo", "ToggleVisibleBookmarkList", "ToggleVisibleNavigator",
        "ToggleVisibleFilmStrip", "ToggleHideFilmStrip", "NextScrollPage", "PrevScrollPage", "JumpPage", "NextSizePage", "PrevSizePage"
    };
    /// <summary>由启动层接入原生事件；只消费主查看器区域，其余控件使用框架输入。</summary>
    public void AttachPlatformInput(IPlatformInput input)
    {
        _platformInput?.Dispose(); _platformInput = input; input.Attach(HandlePlatformGesture);
    }
    /// <summary>真实精确滚动连续平移，捏合围绕当前指针缩放，不使用增量大小猜设备。</summary>
    public bool HandlePlatformGesture(PlatformGesture gesture)
    {
        if (_preparing || !IsActive || OwnedWindows.Any(w => w.IsVisible) || _model?.Operation.Book is null) return false;
        if (gesture.SourceWindow != (TryGetPlatformHandle()?.Handle ?? 0)) return false;
        var point = Viewer.TranslatePoint(new Point(0, 0), this);
        if (point is null) return false;
        var local = new Point(gesture.X - point.Value.X, gesture.Y - point.Value.Y);
        if (!new Avalonia.Rect(Viewer.Bounds.Size).Contains(local)) return false;
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
        AddHandler(DragDrop.DropEvent, Drop);
        DragDrop.SetAllowDrop(this, true);
        Viewer.GestureRequested += async (_, gesture) => await GestureAsync(gesture);
        Viewer.PointerWheelChanged += Viewer_Wheel;
        // 滑条内部 Thumb 会处理指针事件；隧道路由保证释放确认不被模板吞掉。
        var slider = this.FindControl<Slider>("PageSliderView")!;
        slider.AddHandler(PointerPressedEvent, Slider_Pressed, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, Slider_Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closing += Window_Closing;
    }
    /// <summary>由唯一启动层传入已经装配的契约，不在控件中创建解码或存储实现。</summary>
    public void Bind(ReaderWorkspaceViewModel model, BitmapFactory images, IPlatformService platform)
    {
        _model = model; _images = images; _platform = platform; DataContext = model;
        Viewer.Attach(model.Operation, images); model.Refreshed += Model_Refreshed;
        model.PanelsRefreshed += Model_PanelsRefreshed;
        _leftWidth = model.LeftWidth; _rightWidth = model.RightWidth; UpdatePanelColumns(); model.Attach();
        FilmStrip.Attach(model.Operation, images); NavigatorView.Attach(model.Operation, images);
        _sidePanels = new(this, model);
        FilmStrip.PageRequested += async (_, index) => { try { await model.Operation.JumpAsync(index); } catch (Exception ex) { ShowError(ex.Message); } };
        FilmStrip.FrameMoveRequested += async (_, direction) => { try { await model.Operation.MoveAsync(direction); } catch (Exception ex) { ShowError(ex.Message); } };
        FilmStrip.GlobalWheelRequested += FilmStrip_GlobalWheel;
        NavigatorView.NavigateRequested += (_, point) => Viewer.Navigate(point);
        BuildMenus();
    }
    /// <summary>返回真实执行能力，菜单占位与输入状态使用同一判断。</summary>
    public bool IsCommandAvailable(string name) => HostCommands.Contains(name) || _model?.Commands.IsAvailable(name) == true;
    /// <summary>迁入完整菜单后追加 Mac 打开目录及已有交互，保持原八组顺序。</summary>
    private void BuildMenus()
    {
        if (_model is null) return;
        var root = MenuTree.CreateDefault();
        root.Children![0].Children!.Insert(1, new("打开目录…", MenuElementType.Command, "OpenFolder"));
        root.Children[0].Children!.Add(new("重新载入", MenuElementType.Command, "ReLoad"));
        root.Children[0].Children!.Add(new("关闭窗口", MenuElementType.Command, "CloseWindow"));
        foreach (var (text, command) in new[] { ("左侧栏自动隐藏", "LeftAutoHide"), ("右侧栏自动隐藏", "RightAutoHide") })
            root.Children[1].Children!.Add(new(text, MenuElementType.Command, command));
        foreach (var (text, command) in new[] { ("放大", "ViewScaleUp"), ("缩小", "ViewScaleDown") })
            root.Children[2].Children!.Add(new(text, MenuElementType.Command, command));
        // 原默认菜单未列出的定位命令追加到跳转组，原节点/占位和顺序完整保留。
        root.Children[3].Children!.Add(new(null, MenuElementType.Separator, null));
        foreach (var name in new[] { "JumpPage", "PrevHistoryPage", "NextHistoryPage", "PrevBookHistory", "NextBookHistory" })
            root.Children[3].Children!.Add(new(null, MenuElementType.Command, name));
        MenuPresenter.Populate(this.FindControl<Menu>("MenuBar")!, root, _model.Commands, _model.SaveData, IsCommandAvailable, ExecuteAsync, GetCommandCheck);
    }
    /// <summary>原菜单绑定的勾选表现；只读取引擎配置，不在菜单中维护第二套状态。</summary>
    private bool? GetCommandCheck(string name) => name switch
    {
        "SetPageModeOne" => _model?.Operation.Book?.Setting.PageMode == PageMode.SinglePage,
        "SetPageModeTwo" => _model?.Operation.Book?.Setting.PageMode == PageMode.WidePage,
        "SetBookReadOrderRight" => _model?.Operation.Book?.Setting.BookReadOrder == PageReadOrder.RightToLeft,
        "SetBookReadOrderLeft" => _model?.Operation.Book?.Setting.BookReadOrder == PageReadOrder.LeftToRight,
        "ToggleIsSupportedDividePage" => _model?.Divide,
        "ToggleIsSupportedWidePage" => _model?.Wide,
        "ToggleIsSupportedSingleFirstPage" => _model?.FirstSingle,
        "ToggleIsSupportedSingleLastPage" => _model?.LastSingle,
        "ToggleBookmark" => _model?.IsBookmark,
        "ToggleVisibleBookshelf" => _model?.ShowFolderList,
        "ToggleVisiblePageList" => _model?.ShowPageList,
        "ToggleVisibleHistoryList" => _model?.ShowHistory,
        "ToggleVisibleFileInfo" => _model?.ShowInformation,
        "ToggleVisibleBookmarkList" => _model?.ShowBookmarks,
        "ToggleVisibleNavigator" => _model?.ShowNavigator,
        "ToggleVisibleFilmStrip" => Config.Current.FilmStrip.IsEnabled,
        "ToggleHideFilmStrip" => Config.Current.FilmStrip.IsHideFilmStrip,
        "LeftAutoHide" => Config.Current.Panels.IsLeftAutoHide,
        "RightAutoHide" => Config.Current.Panels.IsRightAutoHide,
        "SetStretchModeUniform" => Config.Current.View.StretchMode == PageStretchMode.Uniform,
        "SetStretchModeNone" => Config.Current.View.StretchMode == PageStretchMode.None,
        _ => null
    };
    /// <summary>表现变化只更新列宽；GridSplitter 的实际宽度由窗口保存，控件不设置固定 Width。</summary>
    private void Model_PanelsRefreshed(object? sender, EventArgs e)
    {
        UpdatePanelColumns();
        _sidePanels?.Refresh();
        _ = NavigatorView.RefreshAsync();
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
    }
    /// <summary>隐藏时收起面板列，重新显示恢复用户拖动后的宽度。</summary>
    private void UpdatePanelColumns()
    {
        if (_model is null) return;
        var columns = this.FindControl<Grid>("SidePanelFrame")!.ColumnDefinitions;
        if (columns[1].ActualWidth > 0) _leftWidth = columns[1].ActualWidth;
        if (columns[5].ActualWidth > 0) _rightWidth = columns[5].ActualWidth;
        columns[1].Width = new GridLength(_model.LeftVisible ? Math.Max(100, _leftWidth) : 0);
        columns[5].Width = new GridLength(_model.RightVisible ? Math.Max(100, _rightWidth) : 0);
        columns[2].Width = new GridLength(_model.LeftVisible ? 4 : 0);
        columns[4].Width = new GridLength(_model.RightVisible ? 4 : 0);
    }
    /// <summary>打开请求统一进入 BookOperation，窗口不枚举内容。</summary>
    public async Task OpenAsync(string path)
    {
        if (_model is null) return; Viewer.ResetTransform(); await _model.Operation.OpenAsync(path);
    }
    /// <summary>启动和无窗口重开恢复最后书籍。</summary>
    public Task RestoreLastAsync() => _model?.SaveData.LastBookPath is { } path ? OpenAsync(path) : Task.CompletedTask;
    /// <summary>执行宿主命令或转交原阅读命令；错误显示给用户。</summary>
    public async Task ExecuteAsync(string name)
    {
        if (_model is null) return;
        try
        {
            switch (name)
            {
                case "LoadAs":
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "打开图片、ZIP / RAR / 7z", AllowMultiple = false });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } file) await OpenAsync(file); break;
                case "OpenFolder":
                    var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "打开图片目录", AllowMultiple = false });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) await OpenAsync(folder); break;
                case "ReLoad": if (_model.Operation.Book is { } reload) await OpenAsync(reload.Path); break;
                case "ParentFolder":
                    if (_model.Operation.Book is { } current && System.IO.Path.GetDirectoryName(current.Path) is { } parent) await OpenAsync(parent); break;
                case "OpenExplorer":
                    if (_model.Operation.Book is { } book) await _platform!.RevealAsync(book.CurrentPage?.ArchiveEntry.FilePath ?? book.Path); break;
                case "CloseWindow": Close(); break;
                case "CloseApplication": (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown(); break;
                case "ToggleFullScreen": WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; break;
                case "ViewScaleUp": await Viewer.ZoomAsync(1.2); break;
                case "ViewScaleDown": await Viewer.ZoomAsync(1 / 1.2); break;
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
                case "SetStretchModeUniform": Config.Current.View.StretchMode = PageStretchMode.Uniform; Viewer.ResetTransform(); await Viewer.RefreshAsync(); break;
                case "SetStretchModeNone": Config.Current.View.StretchMode = PageStretchMode.None; Viewer.ResetTransform(); await Viewer.RefreshAsync(); break;
                case "ToggleHideLeftPanel": Config.Current.Panels.IsLeftVisible = !Config.Current.Panels.IsLeftVisible; _model.RefreshPanels(); break;
                case "ToggleHideRightPanel": Config.Current.Panels.IsRightVisible = !Config.Current.Panels.IsRightVisible; _model.RefreshPanels(); break;
                case "LeftAutoHide": Config.Current.Panels.IsLeftAutoHide = !Config.Current.Panels.IsLeftAutoHide; _model.RefreshPanels(); break;
                case "RightAutoHide": Config.Current.Panels.IsRightAutoHide = !Config.Current.Panels.IsRightAutoHide; _model.RefreshPanels(); break;
                case "OpenOptionsWindow":
                    await new SettingsWindow(_model, IsCommandAvailable).ShowDialog(this);
                    _model.RefreshSelection(); _model.RefreshPanels(); await FilmStrip.RefreshAsync(); BuildMenus(); break;
                case "HelpCommandList": await ShowCommandStatusAsync(); break;
                case "ToggleBookmark":
                    if (_model.Operation.Book is { } marked) { await _model.Operation.SaveAsync(); await _model.SaveData.ToggleBookmarkAsync(marked); } break;
                case "LoadRecentBook":
                    var recent = _model.SaveData.HistoryEntries.FirstOrDefault(e => e.Path != _model.Operation.Book?.Path);
                    if (recent is not null) await OpenAsync(recent.Path); break;
                case "OpenBookExplorer": if (_model.Operation.Book is { } source) await _platform!.RevealAsync(source.Path); break;
                case "ToggleVisibleBookshelf": _model.SelectPanel("FolderPanel"); break;
                case "ToggleVisiblePageList": _model.SelectPanel("PageListPanel"); break;
                case "ToggleVisibleHistoryList": _model.SelectPanel("HistoryPanel"); break;
                case "ToggleVisibleFileInfo": _model.SelectPanel("FileInformationPanel"); break;
                case "ToggleVisibleBookmarkList": _model.SelectPanel("BookmarkPanel"); break;
                case "ToggleVisibleNavigator": _model.SelectPanel("NavigatePanel"); break;
                case "ToggleVisibleFilmStrip": Config.Current.FilmStrip.IsEnabled = !Config.Current.FilmStrip.IsEnabled; _model.RefreshPanels(); await FilmStrip.RefreshAsync(); break;
                case "ToggleHideFilmStrip": Config.Current.FilmStrip.IsHideFilmStrip = !Config.Current.FilmStrip.IsHideFilmStrip; _model.RefreshPanels(); break;
                default: await _model.Commands.ExecuteAsync(name); break;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
    }
    /// <summary>业务回报刷新查看器；只在来源改变时更新目录导航。</summary>
    private async void Model_Refreshed(object? sender, EventArgs e)
    {
        if (_preparing || _model is null) return;
        MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
        await Viewer.RefreshAsync();
        await FilmStrip.RefreshAsync(); await NavigatorView.RefreshAsync();
        var book = _model.Operation.Book;
        if (book is null || ReferenceEquals(_folderBook, book)) return;
        _folderBook = book; _folders?.Cancel(); var pending = new CancellationTokenSource(); _folders = pending;
        try
        {
            var folders = await _model.Operation.GetFoldersAsync(pending.Token);
            if (!_preparing && ReferenceEquals(_folderBook, book) && !pending.IsCancellationRequested) _model.SetFolders(folders);
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
    /// <summary>用户选中列表条目后按原索引定位，绑定回报不重复导航。</summary>
    private async void Page_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_model?.SelectedPage is { } page && _model.Operation.Book?.CurrentPage != page)
            try { await _model.Operation.JumpAsync(page.Index); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>目录双击统一打开，控件只持有只读条目。</summary>
    private async void Folder_DoubleTapped(object? sender, TappedEventArgs e) { if (sender is ListBox { SelectedItem: FolderItem item }) await OpenAsync(item.Path); }
    /// <summary>历史打开继续走原恢复策略，失败保持当前书籍。</summary>
    private async void History_DoubleTapped(object? sender, TappedEventArgs e) { if (sender is ListBox { SelectedItem: HistoryEntry entry }) await OpenAsync(entry.Path); }
    /// <summary>文件夹保留展开交互，书籍节点双击打开。</summary>
    private async void Bookmark_DoubleTapped(object? sender, TappedEventArgs e) { if (sender is TreeView { SelectedItem: BookmarkNode { IsFolder: false, Path: { } path } }) await OpenAsync(path); }
    /// <summary>在所选文件夹中创建节点；未选文件夹时使用根目录。</summary>
    private async void Bookmark_NewFolder(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var parent = this.FindControl<TreeView>("BookmarkTree")!.SelectedItem as BookmarkNode;
        var name = await AskNameAsync("新建书签文件夹", "");
        if (name is null) return;
        try { await _model.SaveData.AddBookmarkFolderAsync(parent?.IsFolder == true ? parent : null, name); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>修改所选节点名称，取消不写入状态。</summary>
    private async void Bookmark_Rename(object? sender, RoutedEventArgs e)
    {
        if (_model is null || this.FindControl<TreeView>("BookmarkTree")!.SelectedItem is not BookmarkNode node) return;
        var name = await AskNameAsync("重命名书签", node.DisplayName);
        if (name is null) return;
        try { await _model.SaveData.RenameBookmarkAsync(node, name); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>移除树记录；文件夹包含子项时明确确认范围。</summary>
    private async void Bookmark_Remove(object? sender, RoutedEventArgs e)
    {
        if (_model is null || this.FindControl<TreeView>("BookmarkTree")!.SelectedItem is not BookmarkNode node) return;
        if (!await ConfirmAsync("移除书签", $"移除“{node.DisplayName}”{(node.IsFolder ? "及其全部书签子项" : "")}？")) return;
        try { await _model.SaveData.RemoveBookmarkAsync(node); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>小型命名对话框仅返回用户文本，业务校验由 Engine 完成。</summary>
    private Task<string?> AskNameAsync(string title, string value)
    {
        var input = new TextBox { Text = value }; var cancel = new Button { Content = "取消" }; var save = new Button { Content = "确定" };
        var dialog = new Window { Title = title, Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { input, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, save } } } } };
        cancel.Click += (_, _) => dialog.Close(null); save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.Close(input.Text.Trim()); };
        return dialog.ShowDialog<string?>(this);
    }
    /// <summary>范围明确的确认对话框，关闭或取消返回 false。</summary>
    private Task<bool> ConfirmAsync(string title, string message)
    {
        var cancel = new Button { Content = "取消" }; var accept = new Button { Content = "移除" };
        var dialog = new Window { Title = title, Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 16, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, accept } } } } };
        cancel.Click += (_, _) => dialog.Close(false); accept.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(this);
    }
    /// <summary>进入底栏展开已启用的自动隐藏胶片条。</summary>
    private void FilmStrip_Entered(object? sender, PointerEventArgs e) => _model?.HoverFilmStrip(true);
    /// <summary>离开底栏恢复胶片条隐藏状态。</summary>
    private void FilmStrip_Exited(object? sender, PointerEventArgs e) => _model?.HoverFilmStrip(false);
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
        if (e.Handled || _model is null) return;
        if (e.Key == Key.Escape && _sidePanels?.CancelDrag() == true) { e.Handled = true; return; }
        if (e.KeyModifiers == KeyModifiers.Meta && e.Key is Key.O or Key.W or Key.Q)
        { e.Handled = true; await ExecuteAsync(e.Key == Key.O ? "LoadAs" : e.Key == Key.W ? "CloseWindow" : "CloseApplication"); return; }
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        // 弹出菜单拥有方向键，不能让原阅读快捷键抢走菜单导航/选择。
        if (this.FindControl<Menu>("MenuBar")!.IsOpen || FocusManager?.GetFocusedElement() is MenuItem) return;
        if (FocusManager?.GetFocusedElement() == FilmStrip && FilmStrip.HandleSelectionKey(e)) return;
        if (FocusManager?.GetFocusedElement() == this.FindControl<Slider>("PageSliderView") && e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter)
        { e.Handled = true; await CommitSliderAsync(); return; }
        if (FocusManager?.GetFocusedElement() == this.FindControl<Slider>("PageSliderView") && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down) return;
        var matches = _model.Commands.Definitions.Where(d => _model.SaveData.GetShortcut(d.Name, d.Shortcut).Split(',').Any(s => MatchKey(s, e))).ToArray();
        if (matches.Length == 0) return; e.Handled = true;
        if (matches.Length > 1) { ShowError("快捷键冲突：" + string.Join("、", matches.Select(d => d.Text))); return; }
        await ExecuteAsync(matches[0].Name);
    }
    /// <summary>只规范数字键名称，旧 Control 不转换为 Command。</summary>
    internal static bool MatchKey(string value, KeyEventArgs e)
    {
        try
        {
            var tokens = value.Trim().Split('+');
            // 仅规范旧序列化名称：Control 仍表示 Control，不改变成 Command。
            for (int i = 0; i < tokens.Length - 1; i++)
                tokens[i] = tokens[i] == "Control" ? "Ctrl" : tokens[i] == "Command" ? "Meta" : tokens[i];
            if (tokens[^1].Length == 1 && char.IsAsciiDigit(tokens[^1][0])) tokens[^1] = "D" + tokens[^1];
            return KeyGesture.Parse(string.Join('+', tokens)).Matches(e);
        }
        catch (ArgumentException) { return false; }
    }
    /// <summary>鼠标手势按原差分绑定匹配，冲突不会静默覆盖。</summary>
    private async Task GestureAsync(string gesture)
    {
        if (_model is null) return;
        var matches = _model.Commands.Definitions.Where(d => _model.SaveData.GetShortcut(d.Name, d.Shortcut).Split(',')
            .Any(s => string.Equals(s.Trim().Replace("Control+", "Ctrl+").Replace("Command+", "Meta+"), gesture, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 1) await ExecuteAsync(matches[0].Name);
        else if (matches.Length > 1) ShowError("输入冲突：" + string.Join("、", matches.Select(d => d.Text)));
    }
    /// <summary>命令依赖模式按原修饰键滚轮绑定执行，不绕过配置强制缩放/翻页。</summary>
    private async void FilmStrip_GlobalWheel(object? sender, PointerWheelEventArgs e)
    {
        _wheel += e.Delta.Y; int steps = (int)_wheel; _wheel -= steps;
        var modifiers = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers.Add("Meta");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add("Shift");
        var buttons = e.GetCurrentPoint(FilmStrip).Properties;
        if (buttons.IsLeftButtonPressed) modifiers.Add("LeftButton");
        if (buttons.IsRightButtonPressed) modifiers.Add("RightButton");
        if (buttons.IsMiddleButtonPressed) modifiers.Add("MiddleButton");
        var wheel = string.Join('+', modifiers.Append(steps > 0 ? "WheelUp" : "WheelDown"));
        for (int i = 0; i < Math.Abs(steps); i++) await GestureAsync(wheel);
    }
    /// <summary>普通滚轮按原绑定；正式 Mac 精确滚动由平台桥接提前消费。</summary>
    private async void Viewer_Wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { await Viewer.ZoomAsync(Math.Pow(1.15, e.Delta.Y), e.GetPosition(Viewer)); return; }
        if (Math.Abs(e.Delta.X) > 0) { Viewer.Pan(new(e.Delta.X * 24, 0)); return; }
        _wheel += e.Delta.Y;
        if (Math.Abs(_wheel) < 1) return;
        var direction = Math.Sign(_wheel); _wheel -= direction; await GestureAsync(direction > 0 ? "WheelUp" : "WheelDown");
    }
    /// <summary>Finder 拖入使用与菜单相同的打开链路。</summary>
    private async void Drop(object? sender, DragEventArgs e) { if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path) { e.Handled = true; await OpenAsync(path); } }
    /// <summary>显示完整基线命令及迁移状态，已登记数量不当作功能覆盖率。</summary>
    private Task ShowCommandStatusAsync()
    {
        var text = string.Join('\n', _model!.Commands.Definitions.Select(d => $"{d.Text}  [{d.Name}]  {(IsCommandAvailable(d.Name) ? "已接入，详见验收记录" : d.Stage)}  {_model.SaveData.GetShortcut(d.Name, d.Shortcut)}"));
        return new Window { Title = "命令迁移状态", Width = 680, Height = 600, Content = new ScrollViewer { Content = new TextBlock { Text = text, Margin = new Thickness(16) } } }.ShowDialog(this);
    }
    /// <summary>错误信息只改变表现，不覆盖当前阅读内容。</summary>
    private void ShowError(string message) { if (!_preparing) this.FindControl<TextBlock>("StatusField")!.Text = message; }
    private void LeftRail_Entered(object? sender, PointerEventArgs e) => _model?.Hover(true, true);
    private void RightRail_Entered(object? sender, PointerEventArgs e) => _model?.Hover(false, true);
    /// <summary>延迟收起允许指针从图标栏进入相邻面板。</summary>
    private async void LeftRail_Exited(object? sender, PointerEventArgs e) { await Task.Delay(150); if (!this.FindControl<Border>("LeftPanel")!.IsPointerOver && !((Control)sender!).IsPointerOver) _model?.Hover(true, false); }
    /// <summary>右侧栏与左侧栏采用相同自动隐藏规则。</summary>
    private async void RightRail_Exited(object? sender, PointerEventArgs e) { await Task.Delay(150); if (!this.FindControl<Border>("RightPanel")!.IsPointerOver && !((Control)sender!).IsPointerOver) _model?.Hover(false, false); }
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
        try
        {
            _folders?.Cancel(); _sliderDragging = false;
            if (_model is not null)
            {
                var left = this.FindControl<Border>("LeftPanel")!.Bounds.Width;
                var right = this.FindControl<Border>("RightPanel")!.Bounds.Width;
                if (left > 0) Config.Current.Panels.LeftWidth = left;
                if (right > 0) Config.Current.Panels.RightWidth = right;
                _sidePanels?.SaveWeights();
                // 先完成可靠保存，再退订与释放显示资源；失败不能留下已销毁的阅读窗口。
                await _model.Operation.DisposeAsync();
                _model.Detach(); _model.Refreshed -= Model_Refreshed; _model.PanelsRefreshed -= Model_PanelsRefreshed;
            }
            _sidePanels?.Dispose(); _platformInput?.Dispose(); FilmStrip.Dispose(); NavigatorView.Dispose(); Viewer.Dispose(); _images?.Dispose(); _closedPrepared = true;
        }
        finally { _preparing = false; _shutdown = null; }
    }
}
