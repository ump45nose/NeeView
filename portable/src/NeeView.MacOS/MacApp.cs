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
    private bool _shuttingDown;
    private Task? _openingWindow;
    private bool _explicitOpen;
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
                if (_shuttingDown) return; _shuttingDown = true;
                try { if (_window is not null) await _window.PrepareShutdownAsync(); desktop.Shutdown(); }
                catch (Exception ex) { _shuttingDown = false; System.Diagnostics.Trace.WriteLine(ex); }
            };
            _ = StartAsync();
        }
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
            activation.Activated += async (_, e) =>
            {
                // Finder 明确打开优先于启动默认恢复，即使激活先于窗口初始化完成。
                if (e is FileActivatedEventArgs) _explicitOpen = true;
                // 关闭窗口后的 Dock/Finder 重开恢复旧书；明确打开文件仍由下面的新请求决定。
                await OpenWindowAsync(e is not FileActivatedEventArgs);
                if (e is FileActivatedEventArgs files)
                {
                    foreach (var file in files.Files) if (file.TryGetLocalPath() is { } path && _window is not null) await _window.OpenAsync(path);
                }
                else if (e.Kind == ActivationKind.Reopen && _window is not null) _window.Activate();
            };
        base.OnFrameworkInitializationCompleted();
    }
    /// <summary>处理启动参数，未指定来源时恢复独立 Mac 状态。</summary>
    private async Task StartAsync()
    {
        await OpenWindowAsync(false);
        if (_window is null) return;
        if (InitialPaths.FirstOrDefault() is { } path) await _window.OpenAsync(path);
        else if (!_explicitOpen) await _window.RestoreLastAsync();
    }
    /// <summary>重用进行中的初始化，单窗口入口不增加第二个 Host。</summary>
    public Task OpenWindowAsync(bool restore = true)
    {
        if (_window is not null) { _window.Activate(); return Task.CompletedTask; }
        return _openingWindow ??= CreateWindowAsync(restore);
    }
    /// <summary>装配实例生命周期；用户状态目录与旧重写应用分离。</summary>
    private async Task CreateWindowAsync(bool restore)
    {
        // 初始化任务先交还 UI 循环，确保 _openingWindow 在 finally 清理之前已登记。
        await Task.Yield();
        try
        {
            var directory = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Library", "Application Support", "NeeView.Mac");
            var state = new SaveData(directory); await state.LoadAsync();
            var decoder = new MagickImageDecoder(); var operation = new BookOperation(new Backends.ArchiveFactory(), decoder, state);
            var images = new BitmapFactory(decoder); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
            _window = new MainWindow(); _window.Bind(model, images, new MacPlatformService());
            _window.Closed += (_, _) => _window = null;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = _window;
            _window.Show(); if (restore) await _window.RestoreLastAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(ex);
            new Window { Title = "NeeView 启动失败", Width = 600, Height = 240, Content = new TextBlock { Text = "无法加载状态，原文件保持不变。\n" + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) } }.Show();
        }
        finally { _openingWindow = null; }
    }
}
