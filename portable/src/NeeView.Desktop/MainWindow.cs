using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>主窗口仅调用应用契约，面板虚拟化和查看器共用会话。</summary>
public sealed partial class MainWindow : Window, IReaderDialogs
{
    private readonly ReaderWorkspaceViewModel _workspace;
    private readonly IReaderSession _session;
    private readonly ISettingsStore _settingsStore;
    private readonly IReaderStateStore _states;
    private readonly IFileActionService _files;
    private readonly ILegacyImporter _importer;
    private readonly ReaderView _viewer;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status;
    private readonly TextBox _address;
    private readonly Grid _body;
    private readonly Slider _position;
    private AppSettings _settings = new();
    private bool _updating;
    private bool _closing;
    private bool _closePending;
    private readonly ReaderInputRouter _input;
    private long _displayedGeneration;
    private readonly ReaderNavigationPanel _navigation;
    private readonly ReaderDestinationPanel _destinationPanel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _panelGate = new(1);
    private bool? _leftVisible;
    private bool? _rightVisible;
    private long _openRevision;
    /// <summary>组合独立查看器和面板；只接收应用层接口，由启动层装配。</summary>
    public MainWindow(IReaderSession session, ISettingsStore settings, IReaderStateStore states,
        IFileActionService files, IDestinationFolderService destinations, IFolderNavigator folders,
        IPlatformService platform, ILegacyImporter importer, IImageRequestScheduler scheduler, IImageDecoder decoder)
    {
        // 视觉结构由 XAML 独立定义；宿主只解析稳定插槽和装配应用接口。
        AvaloniaXamlLoader.Load(this);
        _status = this.FindControl<TextBlock>("StatusField")!;
        _address = this.FindControl<TextBox>("AddressField")!;
        _body = this.FindControl<Grid>("BodyGrid")!;
        _position = this.FindControl<Slider>("PositionField")!;
        _scroll = this.FindControl<ScrollViewer>("ViewerScroll")!;
        _session = session; _settingsStore = settings; _states = states; _files = files; _importer = importer;
        _workspace = new(session, settings, states, files, destinations, platform, folders) { Dialogs = this };
        DataContext = _workspace;
        _workspace.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReaderWorkspaceViewModel.Status)) _status.Text = _workspace.Status; };
        _workspace.PanelsChanged += RefreshPanelsAsync;
        _workspace.SettingsChanged += () => Dispatcher.UIThread.Post(() => { if (!_closing) { _settings = _workspace.Settings; ApplySettings(); } });
        _workspace.CloseRequested += Close;
        _workspace.QuitRequested += () => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown();
        _input = new(() => _settings, () => _session.Snapshot, ExecuteAsync, text => _status.Text = text);
        _viewer = new(session, scheduler, decoder) { MouseGesture = _input.GestureAsync };
        _scroll.Content = _viewer;
        _viewer.ScrollRequested += y => { _scroll.Offset = new(_scroll.Offset.X, y); };
        _viewer.PanRequested += (x, y) => _scroll.Offset = new(x, y);
        _scroll.ScrollChanged += (_, _) => _viewer.SetViewport(_scroll.Viewport.Width, _scroll.Viewport.Height, _scroll.Offset.Y, left: _scroll.Offset.X);
        _scroll.SizeChanged += (_, _) => _viewer.SetViewport(_scroll.Bounds.Width, _scroll.Bounds.Height, _scroll.Offset.Y, false, _scroll.Offset.X);
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Open(_address.Text ?? ""); e.Handled = true; } };
        _position.PropertyChanged += async (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updating && _session.Snapshot.Index is { } index && index.Pages.Count > 0)
                await _session.LocateAsync(new(index.Pages[Math.Clamp((int)_position.Value, 0, index.Pages.Count - 1)].Id));
        };
        _navigation = new(_workspace, session, scheduler);
        this.FindControl<ContentControl>("NavigationHost")!.Content = _navigation;
        _destinationPanel = new(_workspace, RefreshDestinationsAsync);
        this.FindControl<ContentControl>("DestinationsHost")!.Content = _destinationPanel;
        _session.Changed += SnapshotChanged;
        AddHandler(KeyDownEvent, (_, e) => _input.KeyDown(this, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _viewer.AddHandler(PointerWheelChangedEvent, (_, e) => _input.Wheel(e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) => { var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath(); if (file is not null) Open(file); });
        Closing += OnClosing;
        Opened += async (_, _) => await ObserveAsync(async () =>
        {
            await _workspace.ReloadSettingsAsync();
            if (_closing) return;
            _settings = _workspace.Settings; ApplySettings();
            _destinationPanel.SectionRatio = _settings.DestinationRatio;
            await RefreshPanelsAsync();
            var pending = await _states.RecoveriesAsync(_lifetime.Token);
            if (!_closing && pending.Count > 0) _status.Text = $"有 {pending.Count} 个中断文件操作，原文件和备份已保留；请在恢复记录中核对。";
        });
    }
    /// <summary>适配 XAML 按钮的稳定命令标识，捕获异步异常；排布与文案不影响命令实现。</summary>
    /// <param name="sender">带有命令 Tag 的工具栏按钮。</param>
    /// <param name="e">按钮点击事件。</param>
    private async void OnToolbarClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command } || _closing) return;
        await ObserveAsync(command switch { "Settings" => EditSettingsAsync, "Import" => ImportAsync, _ => () => ExecuteAsync(command) });
    }
    /// <summary>同步投递阅读表现更新；后台面板任务可等待、可取消，错误统一观察。</summary>
    private void SnapshotChanged(ReaderSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        if (_closing || snapshot.Generation != _session.Snapshot.Generation) return;
        _updating = true;
        try
        {
            _viewer.SetSnapshot(snapshot); _navigation.SetSnapshot(snapshot);
            _position.Maximum = Math.Max(0, (snapshot.Index?.Pages.Count ?? 1) - 1);
            _position.Value = Math.Max(0, snapshot.Index?.Pages.ToList().FindIndex(p => p.Id == snapshot.Anchor?.Content) ?? 0);
            _address.Text = snapshot.Index?.Locator.Path;
            _status.Text = snapshot.Error ?? (snapshot.Loading ? "正在打开…" : $"{snapshot.Current?.Name ?? "空目录"} · {snapshot.Index?.Pages.Count ?? 0} 项 · {ReaderLabels.Text(snapshot.Options.Mode)}");
        }
        finally { _updating = false; }
        if (_displayedGeneration != snapshot.Generation)
        { _displayedGeneration = snapshot.Generation; _ = ObserveAsync(RefreshPanelsAsync); }
    });
    /// <summary>系统事件适配同步入口；打开任务统一观察，业务仍由工作区处理。</summary>
    public void Open(string path) => _ = ObserveAsync(() => OpenAsync(path));
    /// <summary>输入来源路径并等待完整索引；供调用方明确打开生命周期。</summary>
    public Task OpenAsync(string path)
    { if (_closing || string.IsNullOrWhiteSpace(path)) return Task.CompletedTask; ++_openRevision; return _workspace.OpenAsync(path); }
    /// <summary>启动或重开恢复最后来源及其保存位置，返回可等待任务。</summary>
    public Task RestoreLastAsync() => ObserveAsync(async () =>
    { var revision = _openRevision; var settings = await _settingsStore.LoadAsync(_lifetime.Token); if (!_closing && revision == _openRevision && settings.LastSource is { } path) await OpenAsync(path); });
    /// <summary>视图仅将命令转交表现层；替换工具栏不改变命令实现。</summary>
    public Task ExecuteAsync(string command, string? parameter = null) => _closing ? Task.CompletedTask : _workspace.ExecuteAsync(command, parameter);
    /// <summary>刷新分类数据，关闭后不再向控件提交结果。</summary>
    private Task RefreshDestinationsAsync(bool force) => RefreshPanelsCoreAsync(false, force);
    /// <summary>刷新独立面板，串行合并存储访问并拒绝切书晚到的结果。</summary>
    private Task RefreshPanelsAsync() => RefreshPanelsCoreAsync(true, true);
    /// <summary>面板刷新属于窗口生命周期；取消和错误不逃出事件处理器。</summary>
    private async Task RefreshPanelsCoreAsync(bool navigation, bool force)
    {
        if (_closing) return;
        try
        {
            await _panelGate.WaitAsync(_lifetime.Token);
            try
            {
                if (_closing) return;
                var snapshot = _session.Snapshot;
                var destinations = await _workspace.DestinationsAsync(force, _lifetime.Token);
                var data = navigation ? await _workspace.NavigationAsync(snapshot, _lifetime.Token) : null;
                if (_closing || snapshot.Generation != _session.Snapshot.Generation) return;
                _destinationPanel.SetData(destinations);
                if (data is not null) _navigation.SetData(data);
            }
            finally { _panelGate.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closing) _workspace.ReportError(error); }
    }
    /// <summary>窗口事件统一观察异步异常；取消关闭属于正常生命周期。</summary>
    private async Task ObserveAsync(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) when (_closing) { } catch (Exception error) { if (!_closing) _workspace.ReportError(error); } }
    /// <summary>独立设置视图收集用户选择，表现层原子合并配置；当前书籍单独更新。</summary>
    private async Task EditSettingsAsync()
    {
        var selection = await new SettingsWindow(await _settingsStore.LoadAsync(), _session.Snapshot.Options).ShowDialog<SettingsSelection?>(this);
        if (selection is null) return;
        await _workspace.UpdateSettingsAsync(s => s with { Defaults = selection.Settings.Defaults, RestorePolicies = selection.Settings.RestorePolicies,
            Shortcuts = selection.Settings.Shortcuts, CopyMode = selection.Settings.CopyMode, AutoRefreshDestinations = selection.Settings.AutoRefreshDestinations,
            MoveHistoryCapacity = selection.Settings.MoveHistoryCapacity, LeftVisible = selection.Settings.LeftVisible, RightVisible = selection.Settings.RightVisible });
        _settings = _workspace.Settings; ApplySettings();
        await _session.SetOptionsAsync(selection.Current); await RefreshDestinationsAsync(true);
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
            if (await ChoiceDialogAsync("导入预览", preview, "应用导入")) { await _importer.ApplyAsync(plan); await _workspace.ReloadSettingsAsync(); _settings = _workspace.Settings; ApplySettings(); await RefreshPanelsAsync(); _status.Text = "导入完成，原数据未修改。"; }
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    /// <summary>应用开关；仅首次或显隐变化恢复宽度，不覆盖用户正在调整的分隔条。</summary>
    private void ApplySettings()
    {
        _updating = true;
        _destinationPanel.ApplySettings(_settings);
        if (_leftVisible != _settings.LeftVisible) _body.ColumnDefinitions[0].Width = new(_settings.LeftVisible ? _settings.LeftWidth : 0);
        if (_rightVisible != _settings.RightVisible) _body.ColumnDefinitions[4].Width = new(_settings.RightVisible ? _settings.RightWidth : 0);
        _leftVisible = _settings.LeftVisible; _rightVisible = _settings.RightVisible;
        _files.Capacity = _settings.MoveHistoryCapacity; _updating = false;
    }
    /// <summary>保存位置和侧栏比例，调用者可用于关闭或退出。</summary>
    public async Task FlushAsync()
    {
        await _session.FlushAsync();
        // 持久化时合并最新配置；隐藏侧栏不覆盖用户已有宽度。
        _settings = await _settingsStore.UpdateAsync(s => s with {
            LeftWidth = s.LeftVisible ? _body.ColumnDefinitions[0].ActualWidth : s.LeftWidth,
            RightWidth = s.RightVisible ? _body.ColumnDefinitions[4].ActualWidth : s.RightWidth,
            DestinationRatio = _destinationPanel.SectionRatio });
    }
    /// <summary>关闭窗口先保存并释放会话，应用继续驻留。</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return; e.Cancel = true;
        if (_closePending) return; _closePending = true;
        try { await PrepareShutdownAsync(); Close(); }
        catch (Exception error) { _closePending = false; _status.Text = "关闭前保存失败：" + error.Message; }
    }
    /// <summary>停止新界面操作，等待文件提交、保存位置并归还查看器和会话资源。</summary>
    public async Task PrepareShutdownAsync()
    {
        if (_closing) return;
        IsEnabled = false;
        _closing = true; _session.Changed -= SnapshotChanged;
        try
        {
            await _files.DrainAsync(); await FlushAsync(); _lifetime.Cancel();
            await _panelGate.WaitAsync(); _panelGate.Release();
            await _viewer.DisposeAsync(); await _session.DisposeAsync();
        }
        catch { _closing = false; _session.Changed += SnapshotChanged; IsEnabled = true; throw; }
    }
    /// <summary>创建统一异步按钮，错误在当前窗口显示。</summary>
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label }; button.Classes.Add("reader-action");
        button.Click += async (_, _) => await ObserveAsync(action);
        return button;
    }
    /// <summary>选择图片或归档；取消返回空，供表现层调用。</summary>
    public async Task<string?> PickFileAsync() => (await StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = false, Title = "打开图片或压缩包" })).FirstOrDefault()?.TryGetLocalPath();
    /// <summary>选择分类目标目录，取消返回空。</summary>
    public async Task<string?> PickFolderAsync() => (await StorageProvider.OpenFolderPickerAsync(new() { Title = "添加手动目标目录" })).FirstOrDefault()?.TryGetLocalPath();
    /// <summary>将文本输入端口适配到当前窗口对话框。</summary>
    public Task<string?> TextAsync(string title, string value, bool multiline = false) => TextDialogAsync(title, value, multiline);
    /// <summary>将确认端口适配到当前窗口对话框。</summary>
    public Task<bool> ConfirmAsync(string title, string description, string accept) => ChoiceDialogAsync(title, description, accept);
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
    /// <summary>输入确认内容和按钮文字，返回用户选择；对话框作用域隔离主窗口快捷键。</summary>
    private async Task<bool> ChoiceDialogAsync(string title, string description, string accept)
    {
        var dialog = new Window { Title = title, Width = 600, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("取消", () => { dialog.Close(false); return Task.CompletedTask; })); buttons.Children.Add(Button(accept, () => { dialog.Close(true); return Task.CompletedTask; }));
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap } }); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
}
