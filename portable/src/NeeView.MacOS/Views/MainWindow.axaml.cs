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
    private bool _closedPrepared;
    private bool _preparing;
    private Task? _shutdown;
    private CancellationTokenSource? _folders;
    private Book? _folderBook;
    private CancellationTokenSource? _slider;
    private double _wheel;
    private double _leftWidth, _rightWidth;
    public ReaderView Viewer => this.FindControl<ReaderView>("MainViewSocket")!;

    /// <summary>加载可独立验收的布局，设计器和 Headless 不需要具体后端。</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, Key_Down, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, Drop);
        DragDrop.SetAllowDrop(this, true);
        Viewer.GestureRequested += async (_, gesture) => await GestureAsync(gesture);
        Viewer.PointerWheelChanged += Viewer_Wheel;
        Closing += Window_Closing;
    }
    /// <summary>由唯一启动层传入已经装配的契约，不在控件中创建解码或存储实现。</summary>
    public void Bind(ReaderWorkspaceViewModel model, BitmapFactory images, IPlatformService platform)
    {
        _model = model; _images = images; _platform = platform; DataContext = model;
        Viewer.Attach(model.Operation, images); model.Refreshed += Model_Refreshed;
        model.PanelsRefreshed += Model_PanelsRefreshed;
        _leftWidth = model.LeftWidth; _rightWidth = model.RightWidth; UpdatePanelColumns(); model.Attach();
    }
    /// <summary>表现变化只更新列宽；GridSplitter 的实际宽度由窗口保存，控件不设置固定 Width。</summary>
    private void Model_PanelsRefreshed(object? sender, EventArgs e) => UpdatePanelColumns();
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
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "打开图片或 ZIP / CBZ", AllowMultiple = false });
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
                case "SetStretchModeUniform": Config.Current.View.StretchMode = PageStretchMode.Uniform; Viewer.ResetTransform(); await Viewer.RefreshAsync(); break;
                case "SetStretchModeNone": Config.Current.View.StretchMode = PageStretchMode.None; Viewer.ResetTransform(); await Viewer.RefreshAsync(); break;
                case "ToggleHideLeftPanel": Config.Current.Panels.IsLeftVisible = !Config.Current.Panels.IsLeftVisible; _model.RefreshPanels(); break;
                case "ToggleHideRightPanel": Config.Current.Panels.IsRightVisible = !Config.Current.Panels.IsRightVisible; _model.RefreshPanels(); break;
                case "LeftAutoHide": Config.Current.Panels.IsLeftAutoHide = !Config.Current.Panels.IsLeftAutoHide; _model.RefreshPanels(); break;
                case "RightAutoHide": Config.Current.Panels.IsRightAutoHide = !Config.Current.Panels.IsRightAutoHide; _model.RefreshPanels(); break;
                case "OpenOptionsWindow": await new SettingsWindow(_model).ShowDialog(this); break;
                case "HelpCommandList": await ShowCommandStatusAsync(); break;
                default: await _model.Commands.ExecuteAsync(name); break;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>业务回报刷新查看器；只在来源改变时更新目录导航。</summary>
    private async void Model_Refreshed(object? sender, EventArgs e)
    {
        if (_preparing || _model is null) return;
        await Viewer.RefreshAsync();
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
    /// <summary>滑条拖动防抖，连续位置变化不会排满导航队列。</summary>
    private async void Slider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_model is null || _preparing || Math.Abs(e.NewValue - _model.PageIndex) < .5) return;
        _slider?.Cancel(); var pending = new CancellationTokenSource(); _slider = pending;
        try { await Task.Delay(80, pending.Token); await _model.Operation.JumpAsync((int)Math.Round(e.NewValue)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (ReferenceEquals(_slider, pending)) _slider = null; pending.Dispose(); }
    }
    /// <summary>系统 Command 键先处理；编辑控件隔离其余原快捷键。</summary>
    private async void Key_Down(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _model is null) return;
        if (e.KeyModifiers == KeyModifiers.Meta && e.Key is Key.O or Key.W or Key.Q)
        { e.Handled = true; await ExecuteAsync(e.Key == Key.O ? "LoadAs" : e.Key == Key.W ? "CloseWindow" : "CloseApplication"); return; }
        if (FocusManager?.GetFocusedElement() is TextBox) return;
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
        var matches = _model.Commands.Definitions.Where(d => _model.SaveData.GetShortcut(d.Name, d.Shortcut).Split(',').Contains(gesture, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) await ExecuteAsync(matches[0].Name);
        else if (matches.Length > 1) ShowError("输入冲突：" + string.Join("、", matches.Select(d => d.Text)));
    }
    /// <summary>普通滚轮按原绑定，高精度增量保持连续平移；完整触控板桥接属于 P2。</summary>
    private async void Viewer_Wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { await Viewer.ZoomAsync(Math.Pow(1.15, e.Delta.Y), e.GetPosition(Viewer)); return; }
        if (Math.Abs(e.Delta.Y) < 1 || Math.Abs(e.Delta.X) > 0) { Viewer.Pan(e.Delta * 24); return; }
        _wheel += e.Delta.Y;
        if (Math.Abs(_wheel) < 1) return;
        var direction = Math.Sign(_wheel); _wheel -= direction; await GestureAsync(direction > 0 ? "WheelUp" : "WheelDown");
    }
    /// <summary>Finder 拖入使用与菜单相同的打开链路。</summary>
    private async void Drop(object? sender, DragEventArgs e) { if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path) { e.Handled = true; await OpenAsync(path); } }
    /// <summary>显示完整基线命令及迁移状态，已登记数量不当作功能覆盖率。</summary>
    private Task ShowCommandStatusAsync()
    {
        var text = string.Join('\n', _model!.Commands.Definitions.Select(d => $"{d.Text}  [{d.Name}]  {d.Stage}  {d.Shortcut}"));
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
            _folders?.Cancel(); _slider?.Cancel();
            if (_model is not null)
            {
                var left = this.FindControl<Border>("LeftPanel")!.Bounds.Width;
                var right = this.FindControl<Border>("RightPanel")!.Bounds.Width;
                if (left > 0) Config.Current.Panels.LeftWidth = left;
                if (right > 0) Config.Current.Panels.RightWidth = right;
                // 先完成可靠保存，再退订与释放显示资源；失败不能留下已销毁的阅读窗口。
                await _model.Operation.DisposeAsync();
                _model.Detach(); _model.Refreshed -= Model_Refreshed; _model.PanelsRefreshed -= Model_PanelsRefreshed;
            }
            Viewer.Dispose(); _images?.Dispose(); _closedPrepared = true;
        }
        finally { _preparing = false; _shutdown = null; }
    }
}
