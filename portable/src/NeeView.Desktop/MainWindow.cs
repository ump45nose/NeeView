using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

public sealed class FolderNode(string path) : ObservableObject
{
    public string Path { get; } = path;
    public ObservableCollection<FolderNode> Children { get; } = [];
    public bool Loaded { get; set; }
    public override string ToString() => System.IO.Path.GetFileName(Path.TrimEnd('/')) is { Length: > 0 } name ? name : Path;
}
/// <summary>主窗口仅调用应用契约，面板虚拟化和查看器共用会话。</summary>
public sealed class MainWindow : Window
{
    private readonly IReaderSession _session;
    private readonly ISettingsStore _settingsStore;
    private readonly IReaderStateStore _states;
    private readonly IFileActionService _files;
    private readonly IDestinationFolderService _destinations;
    private readonly IFolderNavigator _folders;
    private readonly IPlatformService _platform;
    private readonly ILegacyImporter _importer;
    private readonly ReaderView _viewer;
    private readonly ScrollViewer _scroll;
    private readonly ListBox _pages = new();
    private readonly TreeView _tree = new();
    private readonly ListBox _history = new();
    private readonly ListBox _bookmarks = new();
    private readonly ListBox _managed = new();
    private readonly ListBox _children = new();
    private readonly TextBlock _status = new() { Margin = new(8, 4), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _address = new() { PlaceholderText = "图片、目录或 ZIP / RAR / 7z 路径", MinWidth = 200 };
    private readonly Grid _body = new();
    private readonly Grid _destinationGrid = new();
    private readonly CheckBox _copy = new() { Content = "复制" };
    private readonly CheckBox _autoRefresh = new() { Content = "自动刷新" };
    private readonly Slider _position = new() { Minimum = 0, Maximum = 1 };
    private AppSettings _settings = new();
    private bool _updating;
    private bool _closing;
    private long _displayedGeneration;
    public MainWindow(IReaderSession session, ISettingsStore settings, IReaderStateStore states,
        IFileActionService files, IDestinationFolderService destinations, IFolderNavigator folders,
        IPlatformService platform, ILegacyImporter importer, IImageRequestScheduler scheduler, IImageDecoder decoder)
    {
        _session = session; _settingsStore = settings; _states = states; _files = files; _destinations = destinations;
        _folders = folders; _platform = platform; _importer = importer;
        Title = "NeeView · macOS"; Width = 1280; Height = 860; MinWidth = 800; MinHeight = 480; FontSize = 14;
        _viewer = new(session, scheduler, decoder);
        _scroll = new() { Content = _viewer, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _viewer.ScrollRequested += y => { _scroll.Offset = new(_scroll.Offset.X, y); };
        _scroll.ScrollChanged += (_, _) => _viewer.SetViewport(_scroll.Viewport.Width, _scroll.Viewport.Height, _scroll.Offset.Y);
        _scroll.SizeChanged += (_, _) => _viewer.SetViewport(_scroll.Bounds.Width, _scroll.Bounds.Height, _scroll.Offset.Y, false);
        var root = new DockPanel(); Content = root;
        var toolbar = BuildToolbar(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var bottom = new StackPanel { Orientation = Orientation.Vertical }; DockPanel.SetDock(bottom, Dock.Bottom);
        _position.PropertyChanged += async (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updating && _session.Snapshot.Index is { } index && index.Pages.Count > 0)
                await _session.LocateAsync(new(index.Pages[Math.Clamp((int)_position.Value, 0, index.Pages.Count - 1)].Id));
        };
        bottom.Children.Add(_position); bottom.Children.Add(_status); root.Children.Add(bottom);
        _body.ColumnDefinitions = new("240,5,*,5,260"); root.Children.Add(_body);
        var left = BuildNavigation(scheduler); Grid.SetColumn(left, 0); _body.Children.Add(left);
        var splitter1 = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(splitter1, 1); _body.Children.Add(splitter1);
        Grid.SetColumn(_scroll, 2); _body.Children.Add(_scroll);
        var splitter2 = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(splitter2, 3); _body.Children.Add(splitter2);
        var right = BuildDestination(); Grid.SetColumn(right, 4); _body.Children.Add(right);
        _session.Changed += SnapshotChanged;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _viewer.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) => { var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath(); if (file is not null) Open(file); });
        Closing += OnClosing;
        Opened += async (_, _) =>
        {
            _settings = await _settingsStore.LoadAsync(); ApplySettings();
            await RefreshPanelsAsync();
            var pending = await _states.RecoveriesAsync();
            if (pending.Count > 0) _status.Text = $"有 {pending.Count} 个中断文件操作，原文件和备份已保留；请在恢复记录中核对。";
        };
        BuildNativeMenu();
    }
    /// <summary>构造常用阅读工具栏，所有按钮进入统一命令入口。</summary>
    private Control BuildToolbar()
    {
        var dock = new DockPanel { Margin = new(6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (label, command) in new[] { ("打开", "Open"), ("‹", "PrevPage"), ("›", "NextPage"), ("分页", "Paged"), ("连续", "Continuous"), ("瀑布流", "Masonry"), ("双页", "ToggleDouble"), ("方向", "ToggleDirection"), ("适应", "Fit"), ("100%", "ActualPixels"), ("书签", "Bookmark") })
            buttons.Children.Add(Button(label, () => ExecuteAsync(command)));
        buttons.Children.Add(Button("设置", EditSettingsAsync)); buttons.Children.Add(Button("导入", ImportAsync));
        DockPanel.SetDock(buttons, Dock.Left); dock.Children.Add(buttons); dock.Children.Add(_address);
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Open(_address.Text ?? ""); e.Handled = true; } };
        return dock;
    }
    /// <summary>构造虚拟化页面、目录、历史和书签面板。</summary>
    private Control BuildNavigation(IImageRequestScheduler scheduler)
    {
        var tabs = new TabControl { FontSize = 13 };
        _pages.ItemTemplate = new FuncDataTemplate<PageDescriptor>((page, _) =>
        {
            if (page is null) return new TextBlock();
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(4) };
            panel.Children.Add(new ThumbnailView(page, _session, scheduler));
            panel.Children.Add(new TextBlock { Text = page.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
            return panel;
        });
        _pages.SelectionChanged += async (_, _) => { if (!_updating && _pages.SelectedItem is PageDescriptor page) await _session.LocateAsync(new(page.Id), true); };
        var pagePanel = new DockPanel(); var sort = new ComboBox { ItemsSource = Enum.GetValues<SortMode>(), SelectedItem = SortMode.Entry };
        sort.SelectionChanged += async (_, _) => { if (!_updating && sort.SelectedItem is SortMode mode) await _session.SetOptionsAsync(_session.Snapshot.Options with { Sort = mode }); };
        DockPanel.SetDock(sort, Dock.Top); pagePanel.Children.Add(sort); pagePanel.Children.Add(_pages);
        _tree.ItemTemplate = new FuncTreeDataTemplate<FolderNode>((node, _) => new TextBlock { Text = node?.ToString() }, node => node.Children);
        _tree.SelectionChanged += async (_, _) =>
        {
            if (_tree.SelectedItem is not FolderNode node || _updating) return;
            try
            {
                if (!node.Loaded) { node.Loaded = true; foreach (var path in await _folders.ChildrenAsync(node.Path)) node.Children.Add(new(path)); }
                Open(node.Path);
            }
            catch (Exception error) { _status.Text = error.Message; }
        };
        _history.ItemTemplate = new FuncDataTemplate<ReadingState>((state, _) => new TextBlock { Text = state?.Locator.Path, Margin = new(4), TextTrimming = TextTrimming.CharacterEllipsis });
        _history.DoubleTapped += (_, _) => { if (_history.SelectedItem is ReadingState state) Open(state.Locator.Path); };
        _bookmarks.ItemTemplate = new FuncDataTemplate<Bookmark>((mark, _) => new TextBlock { Text = mark?.Name, Margin = new(4) });
        _bookmarks.DoubleTapped += async (_, _) =>
        {
            if (_bookmarks.SelectedItem is not Bookmark mark || mark.Locator is not { } locator) return;
            await _session.OpenAsync(new(locator.Path));
            if (mark.Anchor is { } anchor) await _session.LocateAsync(anchor);
            else if (mark.LegacyPage is { } legacy && _session.Snapshot.Index?.Pages.FirstOrDefault(p => p.Name.Replace('\\', '/') == legacy.Replace('\\', '/')) is { } page)
                await _session.LocateAsync(new(page.Id));
        };
        tabs.ItemsSource = new[] { new TabItem { Header = "页面", Content = pagePanel }, new TabItem { Header = "目录", Content = _tree }, new TabItem { Header = "历史", Content = _history }, new TabItem { Header = "书签", Content = _bookmarks } };
        return tabs;
    }
    /// <summary>构造上下独立滚动分类区，手动目录不限制数量。</summary>
    private Control BuildDestination()
    {
        var dock = new DockPanel(); var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_copy); actions.Children.Add(_autoRefresh);
        actions.Children.Add(Button("刷新", () => RefreshDestinationsAsync(true)));
        actions.Children.Add(Button("添加目标", AddDestinationAsync)); actions.Children.Add(Button("新建子目录", CreateDestinationAsync));
        actions.Children.Add(Button("撤销", () => ExecuteAsync("UndoDestinationMove"))); actions.Children.Add(Button("重做", () => ExecuteAsync("RedoDestinationMove")));
        actions.Children.Add(Button("Finder", () => ExecuteAsync("Reveal"))); actions.Children.Add(Button("重命名", () => ExecuteAsync("Rename")));
        actions.Children.Add(Button("废纸篓", () => ExecuteAsync("Trash")));
        DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        _destinationGrid.RowDefinitions = new("*,5,*");
        _managed.ItemTemplate = new FuncDataTemplate<string>((path, _) => DestinationLabel(path!, true));
        _children.ItemTemplate = new FuncDataTemplate<string>((path, _) => DestinationLabel(path!, false));
        _managed.DoubleTapped += async (_, _) => { if (_managed.SelectedItem is string path) await ClassifyAsync(path, false); };
        _children.DoubleTapped += async (_, _) => { if (_children.SelectedItem is string path) await ClassifyAsync(path, false); };
        var upper = new DockPanel(); var upperLabel = new TextBlock { Text = "目标目录（前九项对应数字键）", Margin = new(6) }; DockPanel.SetDock(upperLabel, Dock.Top); upper.Children.Add(upperLabel); upper.Children.Add(_managed);
        var lower = new DockPanel(); var lowerLabel = new TextBlock { Text = "当前目录的直接子目录", Margin = new(6) }; DockPanel.SetDock(lowerLabel, Dock.Top); lower.Children.Add(lowerLabel); lower.Children.Add(_children);
        Grid.SetRow(upper, 0); Grid.SetRow(lower, 2); _destinationGrid.Children.Add(upper);
        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); _destinationGrid.Children.Add(split); _destinationGrid.Children.Add(lower); dock.Children.Add(_destinationGrid);
        _copy.IsCheckedChanged += async (_, _) => { if (!_updating) { _settings = _settings with { CopyMode = _copy.IsChecked == true }; await _settingsStore.SaveAsync(_settings); } };
        _autoRefresh.IsCheckedChanged += async (_, _) => { if (!_updating) { _settings = _settings with { AutoRefreshDestinations = _autoRefresh.IsChecked == true }; await _settingsStore.SaveAsync(_settings); await RefreshDestinationsAsync(true); } };
        return dock;
    }
    /// <summary>目录路径作为提示保留，数字编号只标记前九项。</summary>
    private Control DestinationLabel(string path, bool managed)
    {
        var index = _destinations.Managed.ToList().IndexOf(path);
        var label = new TextBlock { Text = (managed && index < 9 ? $"{index + 1} · " : "") + Path.GetFileName(path.TrimEnd('/')), Margin = new(6) };
        ToolTip.SetTip(label, path); return label;
    }
    /// <summary>将快照切到 UI 线程，更新视图和面板。</summary>
    private void SnapshotChanged(ReaderSnapshot snapshot) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closing) return;
        _updating = true;
        try
        {
            _viewer.SetSnapshot(snapshot);
            if (!ReferenceEquals(_pages.ItemsSource, snapshot.Index?.Pages)) _pages.ItemsSource = snapshot.Index?.Pages;
            _pages.SelectedItem = snapshot.Current;
            _position.Maximum = Math.Max(0, (snapshot.Index?.Pages.Count ?? 1) - 1);
            _position.Value = Math.Max(0, snapshot.Index?.Pages.ToList().FindIndex(p => p.Id == snapshot.Anchor?.Content) ?? 0);
            _address.Text = snapshot.Index?.Locator.Path;
            _status.Text = snapshot.Error ?? (snapshot.Loading ? "正在打开…" : $"{snapshot.Current?.Name ?? "空目录"} · {snapshot.Index?.Pages.Count ?? 0} 项 · {snapshot.Options.Mode}");
        }
        finally { _updating = false; }
        await RefreshDestinationsAsync(false);
        if (_displayedGeneration != snapshot.Generation) { _displayedGeneration = snapshot.Generation; await RefreshPanelsAsync(); }
    });
    public void Open(string path) { if (!string.IsNullOrWhiteSpace(path)) _ = _session.OpenAsync(new(path)); }
    /// <summary>启动时恢复最后来源及其保存的位置。</summary>
    public async void RestoreLast() { var settings = await _settingsStore.LoadAsync(); if (settings.LastSource is { } path) Open(path); }
    /// <summary>执行稳定命令；系统命令与阅读/文件目标分开。</summary>
    public async Task ExecuteAsync(string command, string? parameter = null)
    {
        try
        {
            var snapshot = _session.Snapshot; var options = snapshot.Options;
            switch (command)
            {
                case "NextPage": case "MoveNext": await _session.NavigateAsync(1); break;
                case "PrevPage": case "MovePrev": await _session.NavigateAsync(-1); break;
                case "NextOnePage": await _session.NavigateAsync(1, true); break;
                case "PrevOnePage": await _session.NavigateAsync(-1, true); break;
                case "FirstPage": if (snapshot.Index?.Pages.FirstOrDefault() is { } first) await _session.LocateAsync(new(first.Id)); break;
                case "LastPage": if (snapshot.Index?.Pages.LastOrDefault() is { } last) await _session.LocateAsync(new(last.Id)); break;
                case "Open": var selected = await StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = false, Title = "打开图片或压缩包" }); if (selected.FirstOrDefault()?.TryGetLocalPath() is { } file) Open(file); break;
                case "Paged": case "Continuous": case "Masonry": await _session.SetOptionsAsync(options with { Mode = Enum.Parse<ReaderMode>(command) }); break;
                case "ToggleDouble": await _session.SetOptionsAsync(options with { DoublePage = !options.DoublePage }); break;
                case "ToggleDirection": await _session.SetOptionsAsync(options with { Direction = options.Direction == ReadDirection.RightToLeft ? ReadDirection.LeftToRight : ReadDirection.RightToLeft }); break;
                case "Fit": await _session.SetOptionsAsync(options with { Scale = ScaleMode.Fit, Zoom = 1 }); break;
                case "ActualPixels": await _session.SetOptionsAsync(options with { Scale = ScaleMode.ActualPixels, Zoom = 1 }); break;
                case "ZoomIn": case "ZoomOut": await _session.SetOptionsAsync(options with { Zoom = Math.Clamp(options.Zoom * (command == "ZoomIn" ? 1.15 : 1 / 1.15), 0.1, 8) }); break;
                case "Rotate": await _session.SetOptionsAsync(options with { Rotation = (options.Rotation + 90) % 360 }); break;
                case "Bookmark":
                    if (snapshot.Index is { } index) { var name = await TextDialogAsync("书签名称", snapshot.Current?.Name ?? Path.GetFileName(index.Locator.Path)); if (name is not null) await _states.SaveBookmarkAsync(new(Guid.NewGuid().ToString("N"), null, (await _states.BookmarksAsync()).Count, name, index.Book, index.Locator, snapshot.Anchor)); await RefreshPanelsAsync(); } break;
                case "Reveal": if (snapshot.ActionTarget is { } reveal) await _platform.RevealAsync(reveal.Locator.Path); break;
                case "Rename":
                    if (snapshot.ActionTarget is { } rename) { var name = await TextDialogAsync("新的文件名", rename.Name); if (name is not null && Path.GetFileName(name) == name) await FileActionAsync(rename, FileActionKind.Rename, Path.Combine(Path.GetDirectoryName(rename.Locator.Path)!, name)); } break;
                case "Trash": if (snapshot.ActionTarget is { } trash) await FileActionAsync(trash, FileActionKind.Trash, null); break;
                case "Copy": case "MoveToFolderAs": if (parameter is not null) await ClassifyAsync(parameter, command == "MoveToFolderAs"); break;
                case "UndoDestinationMove": case "RedoDestinationMove":
                    var generation = snapshot.Generation;
                    var undo = command == "UndoDestinationMove";
                    var result = undo ? await _files.UndoAsync() : await _files.RedoAsync();
                    if (!result.Success && result.Error?.Contains("同名") == true && await ConfirmOverwriteAsync()) result = undo ? await _files.UndoAsync(ConflictChoice.Overwrite) : await _files.RedoAsync(ConflictChoice.Overwrite);
                    _status.Text = result.Success ? $"已{(undo ? "撤销" : "重做")}：{result.Target}" : result.Error;
                    if (result.Success && generation == _session.Snapshot.Generation)
                    {
                        var indexNow = _session.Snapshot.Index;
                        var restored = result.Target is { } target && Path.GetDirectoryName(target) == indexNow?.Locator.Path ? result.Content : null;
                        await _session.RefreshAsync(restored);
                    }
                    break;
                case "Close": Close(); break;
                case "Quit": await FlushAsync(); (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown(); break;
                default:
                    if (command.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal) && int.TryParse(command[23..], out var number) && number is >= 1 and <= 9 && _destinations.Managed.ElementAtOrDefault(number - 1) is { } destination) await ClassifyAsync(destination, false);
                    else _status.Text = $"当前未支持命令：{command}"; break;
            }
        }
        catch (Exception error) { _status.Text = ReaderException.From(error).Message; }
    }
    /// <summary>根据输入作用域匹配键位；文本输入不触发分类或翻页。</summary>
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || FocusManager?.GetFocusedElement() is TextBox) return;
        foreach (var binding in _settings.Shortcuts)
        {
            if (!TryGesture(binding.Gesture, out var gesture) || !gesture.Matches(e)) continue;
            var conflicts = CommandCatalog.Conflicts(_settings.Shortcuts).Where(c => c.StartsWith(binding.Gesture + ":", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (conflicts.Length > 0) { _status.Text = string.Join("; ", conflicts); e.Handled = true; return; }
            e.Handled = true; await ExecuteAsync(binding.Command, binding.Parameter); return;
        }
    }
    /// <summary>兼容原 Control/Command 和数字键名，不修改保存的原始绑定。</summary>
    private static bool TryGesture(string text, out KeyGesture gesture)
    {
        try
        {
            if (text.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase)) { gesture = null!; return false; }
            var normalized = text.Replace("Control+", "Ctrl+", StringComparison.OrdinalIgnoreCase).Replace("Command+", "Meta+", StringComparison.OrdinalIgnoreCase);
            var parts = normalized.Split('+'); if (parts[^1].Length == 1 && char.IsAsciiDigit(parts[^1][0])) parts[^1] = "D" + parts[^1];
            gesture = KeyGesture.Parse(string.Join('+', parts)); return true;
        }
        catch (ArgumentException) { gesture = null!; return false; }
    }
    /// <summary>精细触控板保留连续滚动；离散鼠标按键位表执行。</summary>
    private async void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        { e.Handled = true; await ExecuteAsync(e.Delta.Y > 0 ? "ZoomIn" : "ZoomOut"); return; }
        if (_session.Snapshot.Options.Mode != ReaderMode.Paged) return;
        if (Math.Abs(e.Delta.Y) < 0.9) return;
        var binding = _settings.Shortcuts.FirstOrDefault(b => b.Gesture == (e.Delta.Y < 0 ? "WheelDown" : "WheelUp"));
        if (binding is not null) { e.Handled = true; await ExecuteAsync(binding.Command); }
    }
    /// <summary>分类目标固定为命令开始时的有效对象；复制不推进，移动重建索引。</summary>
    private async Task ClassifyAsync(string target, bool fixedMove)
    {
        if (_session.Snapshot.ActionTarget is not { } page) { _status.Text = "请先明确选择图片。"; return; }
        await FileActionAsync(page, !fixedMove && _settings.CopyMode ? FileActionKind.Copy : FileActionKind.Move, target);
    }
    private async Task FileActionAsync(PageDescriptor page, FileActionKind action, string? target)
    {
        var generation = _session.Snapshot.Generation;
        var result = await _files.ExecuteAsync(page, action, target);
        if (!result.Success && result.Error?.Contains("同名") == true && await ConfirmOverwriteAsync()) result = await _files.ExecuteAsync(page, action, target, ConflictChoice.Overwrite);
        _status.Text = result.Success ? $"完成：{result.Target ?? result.Source}" : result.Error;
        if (result.Success && action != FileActionKind.Copy && generation == _session.Snapshot.Generation) await _session.RefreshAsync();
    }
    /// <summary>刷新目标目录，只有目录变化才触发自动枚举。</summary>
    private async Task RefreshDestinationsAsync(bool force)
    {
        var page = _session.Snapshot.ActionTarget ?? _session.Snapshot.Current;
        var directory = page?.Locator.Entry is null && page is not null ? Path.GetDirectoryName(page.Locator.Path) : null;
        try { await _destinations.RefreshAsync(directory, force); _managed.ItemsSource = _destinations.Managed; _children.ItemsSource = _destinations.Children; }
        catch (Exception error) { _status.Text = error.Message; }
    }
    /// <summary>更新历史、书签和目录根；只在书籍路径变化时建立新目录节点。</summary>
    private async Task RefreshPanelsAsync()
    {
        _history.ItemsSource = await _states.HistoryAsync(); _bookmarks.ItemsSource = await _states.BookmarksAsync();
        var path = _session.Snapshot.Index?.Locator.Path;
        if (path is not null)
        {
            var directory = _session.Snapshot.Index!.Capabilities.IsArchive ? Path.GetDirectoryName(path)! : path;
            var root = new FolderNode(Path.GetDirectoryName(directory) ?? directory);
            foreach (var child in await _folders.ChildrenAsync(root.Path)) root.Children.Add(new(child)); root.Loaded = true;
            _tree.ItemsSource = new[] { root };
        }
    }
    private async Task AddDestinationAsync()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new() { Title = "添加手动目标目录" });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _settings = _settings with { DestinationFolders = [.. _settings.DestinationFolders, path] }; await _settingsStore.SaveAsync(_settings); await RefreshDestinationsAsync(true);
    }
    private async Task CreateDestinationAsync()
    {
        var page = _session.Snapshot.ActionTarget;
        if (page is null || page.Locator.Entry is not null) return;
        var generation = _session.Snapshot.Generation;
        var name = await TextDialogAsync("新建直接子目录", ""); if (name is null) return;
        var child = await _destinations.CreateChildAsync(Path.GetDirectoryName(page.Locator.Path)!, name);
        if (generation == _session.Snapshot.Generation && _session.Snapshot.ActionTarget?.Id == page.Id) await ClassifyAsync(child, false);
        await RefreshDestinationsAsync(true);
    }
    /// <summary>配置编辑器保存完整独立配置，显示键位冲突后拒绝静默覆盖。</summary>
    private async Task EditSettingsAsync()
    {
        var json = await TextDialogAsync("设置（JSON，可配置快捷键、默认阅读和目标目录）", JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }), true);
        if (json is null) return;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new InvalidDataException("配置为空。");
            var conflicts = CommandCatalog.Conflicts(settings.Shortcuts);
            if (conflicts.Count > 0) { _status.Text = string.Join("; ", conflicts); return; }
            _settings = settings; await _settingsStore.SaveAsync(settings); ApplySettings(); await RefreshDestinationsAsync(true);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    /// <summary>先生成旧数据导入预览，再应用明确选择的计划。</summary>
    private async Task ImportAsync()
    {
        var source = await TextDialogAsync("导入 .nvzip 或旧 Profile 目录", ""); if (source is null) return;
        var mapping = await TextDialogAsync("路径映射，每行 Windows前缀 => macOS前缀", "C:\\Books => /Users/" + Environment.UserName + "/Pictures", true);
        if (mapping is null) return;
        try
        {
            var maps = mapping.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split("=>", 2)).Where(parts => parts.Length == 2).Select(parts => new PathMapping(parts[0].Trim(), parts[1].Trim())).ToArray();
            var plan = await _importer.PlanImportAsync(source, maps);
            var preview = $"历史 {plan.Books.Count}；书签节点 {plan.Bookmarks.Count}\n{string.Join('\n', plan.Warnings)}";
            if (await ChoiceDialogAsync("导入预览", preview, "应用导入")) { await _importer.ApplyAsync(plan); _settings = await _settingsStore.LoadAsync(); ApplySettings(); await RefreshPanelsAsync(); _status.Text = "导入完成，原数据未修改。"; }
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private void ApplySettings()
    {
        _updating = true;
        _copy.IsChecked = _settings.CopyMode; _autoRefresh.IsChecked = _settings.AutoRefreshDestinations;
        _body.ColumnDefinitions[0].Width = new(_settings.LeftVisible ? _settings.LeftWidth : 0);
        _body.ColumnDefinitions[4].Width = new(_settings.RightVisible ? _settings.RightWidth : 0);
        _destinationGrid.RowDefinitions[0].Height = new(_settings.DestinationRatio, GridUnitType.Star);
        _destinationGrid.RowDefinitions[2].Height = new(1 - _settings.DestinationRatio, GridUnitType.Star);
        _files.Capacity = _settings.MoveHistoryCapacity; _updating = false;
    }
    /// <summary>保存位置和侧栏比例，调用者可用于关闭或退出。</summary>
    public async Task FlushAsync()
    {
        await _session.FlushAsync();
        var height = _destinationGrid.RowDefinitions[0].ActualHeight + _destinationGrid.RowDefinitions[2].ActualHeight;
        var persisted = await _settingsStore.LoadAsync();
        _settings = persisted with { LeftWidth = _body.ColumnDefinitions[0].ActualWidth, RightWidth = _body.ColumnDefinitions[4].ActualWidth,
            DestinationRatio = height > 0 ? _destinationGrid.RowDefinitions[0].ActualHeight / height : 0.5 };
        await _settingsStore.SaveAsync(_settings);
    }
    /// <summary>关闭窗口先保存并释放会话，应用继续驻留。</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return; e.Cancel = true;
        await FlushAsync(); _closing = true; _session.Changed -= SnapshotChanged; _viewer.Dispose(); await _session.DisposeAsync(); Close();
    }
    private static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new(2) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is MainWindow window) window._status.Text = error.Message; } };
        return button;
    }
    /// <summary>文本对话框作用域独立于主窗口快捷键。</summary>
    private async Task<string?> TextDialogAsync(string title, string value, bool multiline = false)
    {
        var dialog = new Window { Title = title, Width = multiline ? 700 : 500, Height = multiline ? 560 : 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap };
        var panel = new DockPanel { Margin = new(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("取消", () => { dialog.Close(); return Task.CompletedTask; })); buttons.Children.Add(Button("确定", () => { dialog.Close(input.Text); return Task.CompletedTask; }));
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(input); dialog.Content = panel;
        dialog.Opened += (_, _) => input.Focus(); return await dialog.ShowDialog<string?>(this);
    }
    private Task<bool> ConfirmOverwriteAsync() => ChoiceDialogAsync("同名冲突", "覆盖前将保留可恢复备份。", "覆盖");
    private async Task<bool> ChoiceDialogAsync(string title, string description, string accept)
    {
        var dialog = new Window { Title = title, Width = 600, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("取消", () => { dialog.Close(false); return Task.CompletedTask; })); buttons.Children.Add(Button(accept, () => { dialog.Close(true); return Task.CompletedTask; }));
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap } }); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    /// <summary>原生菜单统一调用同一命令；关闭窗口与退出保持不同语义。</summary>
    private void BuildNativeMenu()
    {
        var menu = new NativeMenu(); var application = new NativeMenu();
        foreach (var (name, command) in new[] { ("打开…", "Open"), ("关闭窗口", "Close"), ("退出 NeeView", "Quit") })
        { var item = new NativeMenuItem(name); item.Click += async (_, _) => await ExecuteAsync(command); application.Items.Add(item); }
        menu.Items.Add(new NativeMenuItem("NeeView") { Menu = application }); NativeMenu.SetMenu(this, menu);
    }
}
