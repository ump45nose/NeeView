using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS;

/// <summary>唯一正式启动装配；具体后端只在此处接入。</summary>
public sealed partial class MacApp : Avalonia.Application
{
    public static string[] InitialPaths { get; set; } = [];
    private MainWindow? _window;
    private bool _shuttingDown, _quitWaiting;
    private Task? _profileImportTask;
    private Task? _openingWindow;
    private bool _explicitOpen;
    // 启动装配持有唯一具体后端，向各业务分别注入文件与重命名能力契约。
    private FileOperationBackend? _fileOperations;
    private DestinationMoveService? _destinationMoves;
    private ArchiveEntryRealizer? _entryRealizer;
    private TemporaryPlaylistService? _temporaryPlaylists;
    private ContentDropReceiver? _contentDropReceiver;
    /// <summary>加载转换的原主题资源和原生应用菜单。</summary>
    public override void Initialize()
    {
        Name = "NeeView"; RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        AvaloniaXamlLoader.Load(this);
        var menu = new NativeMenu(); var appMenu = new NativeMenu();
        var open = new NativeMenuItem("打开…"); open.Click += async (_, _) => { await OpenWindowAsync(); if (_window is not null) await _window.ExecuteAsync("LoadAs"); };
        var reopen = new NativeMenuItem("显示阅读窗口"); reopen.Click += async (_, _) => await OpenWindowAsync();
        var quit = new NativeMenuItem("退出 NeeView") { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Q, Avalonia.Input.KeyModifiers.Meta) };
        quit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown();
        appMenu.Items.Add(open); appMenu.Items.Add(reopen); appMenu.Items.Add(quit); menu.Items.Add(new NativeMenuItem("NeeView") { Menu = appMenu }); NativeMenu.SetMenu(this, menu);
    }
    /// <summary>启动和 Finder 激活共用一个窗口初始化任务，避免重复装配。</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += async (_, e) =>
            {
                e.Cancel = true;
                if (_shuttingDown || _quitWaiting) return; _quitWaiting = true;
                try
                {
                    // 退出先等导入提交/回滚和重建完成，不与五文件事务并发。
                    if (_profileImportTask is { } importing) await importing;
                    _shuttingDown = true;
                    if (_window is not null) await _window.PrepareShutdownAsync();
                    // 初始化也属于进程生命周期；等待恢复操作结束，禁止退出后晚到创建后端/窗口。
                    if (_openingWindow is { } opening) await opening;
                    if (_window is not null) { await _window.PrepareShutdownAsync(); _window.Close(); }
                    // 已释放实例不留给退出失败后的窗口重开；清理失败实例仍保留供重试。
                    if (_entryRealizer is not null) { await _entryRealizer.DisposeAsync(); _entryRealizer = null; }
                    if (_temporaryPlaylists is not null) { await _temporaryPlaylists.DisposeAsync(); _temporaryPlaylists = null; }
                    if (_contentDropReceiver is not null) { await _contentDropReceiver.DisposeAsync(); _contentDropReceiver = null; }
                    desktop.Shutdown();
                }
                catch (Exception ex) { _shuttingDown = false; _quitWaiting = false; System.Diagnostics.Trace.WriteLine(ex); }
            };
            _ = StartAsync();
        }
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
            activation.Activated += async (_, e) =>
            {
                if (_shuttingDown || _quitWaiting) return;
                // Finder 明确打开优先于启动默认恢复，即使激活先于窗口初始化完成。
                if (e is FileActivatedEventArgs) _explicitOpen = true;
                // 关闭窗口后的 Dock/Finder 重开恢复旧书；明确打开文件仍由下面的新请求决定。
                await OpenWindowAsync(e is not FileActivatedEventArgs);
                if (_shuttingDown) return;
                if (e is FileActivatedEventArgs files)
                {
                    if (_window is not null) await _window.OpenFilesAsync(files.Files.Select(file => file.TryGetLocalPath()).OfType<string>());
                }
                else if (e.Kind == ActivationKind.Reopen && _window is not null) _window.Activate();
            };
        base.OnFrameworkInitializationCompleted();
    }
    /// <summary>处理启动参数，未指定来源时恢复独立 Mac 状态。</summary>
    private async Task StartAsync()
    {
        await OpenWindowAsync(false);
        if (_window is null || _shuttingDown) return;
        if (InitialPaths.Length > 0) await _window.OpenFilesAsync(InitialPaths);
        else if (!_explicitOpen) await _window.RestoreLastAsync();
    }
    /// <summary>重用进行中的初始化，单窗口入口不增加第二个 Host。</summary>
    public Task OpenWindowAsync(bool restore = true)
    {
        if (_shuttingDown || _quitWaiting) return Task.CompletedTask;
        if (_profileImportTask is { } importing) return OpenAfterProfileImportAsync(importing, restore);
        if (_window is not null) { _window.Activate(); return Task.CompletedTask; }
        return _openingWindow ??= CreateWindowAsync(restore);
    }
    /// <summary>装配实例生命周期；用户状态目录与旧重写应用分离。</summary>
    private async Task CreateWindowAsync(bool restore, bool propagateFailure = false)
    {
        // 初始化任务先交还 UI 循环，确保 _openingWindow 在 finally 清理之前已登记。
        await Task.Yield();
        BookOperation? operation = null; BitmapFactory? images = null;
        ReaderWorkspaceViewModel? model = null; MainWindow? candidate = null;
        try
        {
            var directory = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Library", "Application Support", "NeeView.Mac");
            var state = new SaveData(directory, Backends.ArchiveFactory.TemporaryDirectory); await state.LoadAsync();
            if (_shuttingDown) return;
            IReadOnlyList<string> recovery = [];
            if (_fileOperations is null) { _fileOperations = new FileOperationBackend(Path.Combine(directory, "FileRecovery")); recovery = await _fileOperations.RecoverAsync(); }
            recovery = recovery.Concat(await state.RecoverBookRenameAsync(_fileOperations)).ToArray();
            if (_shuttingDown) return;
            var decoder = new MagickImageDecoder(); operation = new BookOperation(new Backends.ArchiveFactory(MacFileAliases.Resolve), decoder, state);
            _destinationMoves ??= new(_fileOperations);
            images = new BitmapFactory(decoder); operation.AttachFileOperations(_destinationMoves, _fileOperations, images);
            operation.AttachFileClipboard(new MacFileClipboard());
            _entryRealizer ??= new ArchiveEntryRealizer(); operation.AttachArchiveEntryRealizer(_entryRealizer);
            _temporaryPlaylists ??= new TemporaryPlaylistService(); operation.AttachTemporaryPlaylists(_temporaryPlaylists);
            _contentDropReceiver ??= new ContentDropReceiver(); operation.AttachContentDropReceiver(_contentDropReceiver);
            model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
            var platform = new MacPlatformService();
            candidate = new MainWindow(); _window = candidate; candidate.Bind(model, images, platform);
            _window.AttachProfileImport(new ProfileImportReader(), request => ApplyProfileImportAsync(state, request));
            _window.AttachPlatformInput(new MacTrackpadInput());
            var boundWindow = _window;
            boundWindow.Closed += (_, _) => { if (ReferenceEquals(_window, boundWindow)) _window = null; };
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = _window;
            _window.Show(); RuntimeDiagnostics.Attach(_window, images); if (restore) await _window.RestoreLastAsync();
            _window.ReportFileRecovery(recovery);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(ex);
            try
            {
                // Bind 首先接管 model/images；构造视图前失败的资源仍由启动层所有。
                if (candidate is not null) { await candidate.PrepareShutdownAsync(); candidate.Close(); }
                else
                {
                    if (operation is not null) await operation.DisposeAsync();
                    model?.Detach(); images?.Dispose();
                }
                if (ReferenceEquals(_window, candidate)) _window = null;
            }
            catch (Exception release)
            {
                // 未确认释放的实例仍可能写状态，禁止由导入协调器恢复磁盘。
                throw new ProfileImportRecoveryBlockedException("失败窗口不能安全释放。", new AggregateException(ex, release));
            }
            if (propagateFailure) throw;
            if (!_shuttingDown)
                new Window { Title = "NeeView 启动失败", Width = 600, Height = 240, Content = new TextBlock { Text = "无法加载状态，原文件保持不变。\n" + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) } }.Show();
        }
        finally { _openingWindow = null; }
    }
    /// <summary>Finder/Dock 请求等待导入后再进入同一个窗口入口，不丢弃明确文件打开事件。</summary>
    private async Task OpenAfterProfileImportAsync(Task importing, bool restore)
    { await importing; if (!_shuttingDown && !_quitWaiting) await OpenWindowAsync(restore); }

    /// <summary>启动层持有导入互斥任务，Engine 管事务，现有正式装配路径重建全部配置引用。</summary>
    private Task ApplyProfileImportAsync(SaveData state, ProfileImportRequest request) => _profileImportTask ??= ImportProfileCoreAsync(state, request);
    private async Task ImportProfileCoreAsync(SaveData state, ProfileImportRequest request)
    {
        await Task.Yield();
        try
        {
            if (_openingWindow is { } opening) await opening;
            var coordinator = new ProfileImportCoordinator(state, async () =>
            {
                if (_window is not { } current) throw new InvalidOperationException("阅读窗口已关闭，请重新预览。");
                await current.PrepareShutdownAsync(); current.Close();
            }, () => CreateWindowAsync(true, propagateFailure: true));
            var result = await coordinator.ApplyAsync(request);
            _window?.ReportProfileImport("已导入 " + string.Join("、", result.AppliedFiles) + "；原状态备份：" + result.BackupDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(ex);
            if (_window is not null) _window.ReportProfileImport("导入未完成：" + ex.Message);
            else new Window { Title = "导入恢复未完成", Width = 660, Height = 240, Content = new TextBlock { Text = "请保留 Application Support/NeeView.Mac 中的 ImportBackups 与事务标记。\n" + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(20) } }.Show();
        }
        finally { _profileImportTask = null; }
    }

}
