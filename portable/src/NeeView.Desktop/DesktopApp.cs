using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using NeeView.Application;
using Avalonia.Platform.Storage;

namespace NeeView.Desktop;

/// <summary>共享 Avalonia 启动层；具体服务由 Host 注册。</summary>
public sealed class DesktopApp : Avalonia.Application
{
    public static ServiceProvider Services { get; set; } = null!;
    public static string[] InitialPaths { get; set; } = [];
    public static Func<Task>? ShutdownServices { get; set; }
    private MainWindow? _window;
    private bool _shuttingDown;
    /// <summary>共享主题独立装载，修改资源不改变命令或会话。</summary>
    public override void Initialize()
    {
        Name = "NeeView"; Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://NeeView.Desktop/")) { Source = new Uri("avares://NeeView.Desktop/Styles/ReaderTheme.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        var menu = new NativeMenu(); var application = new NativeMenu();
        var open = new NativeMenuItem("打开…"); open.Click += async (_, _) => { OpenWindow(); await _window!.ExecuteAsync("Open"); };
        var reopen = new NativeMenuItem("显示阅读窗口"); reopen.Click += (_, _) => OpenWindow();
        var quit = new NativeMenuItem("退出 NeeView") { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Q, Avalonia.Input.KeyModifiers.Meta) };
        quit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown();
        application.Items.Add(open); application.Items.Add(reopen); application.Items.Add(quit);
        menu.Items.Add(new NativeMenuItem("NeeView") { Menu = application }); NativeMenu.SetMenu(this, menu);
    }
    /// <summary>创建首个窗口并将启动/Finder 事件送入统一打开流程。</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            OpenWindow(false); desktop.MainWindow = _window;
            desktop.ShutdownRequested += async (_, e) =>
            {
                e.Cancel = true;
                if (_shuttingDown) return; _shuttingDown = true;
                try
                {
                    if (_window is not null) await _window.PrepareShutdownAsync();
                    if (ShutdownServices is not null) await ShutdownServices(); desktop.Shutdown();
                }
                catch (Exception error) { _shuttingDown = false; System.Diagnostics.Trace.WriteLine("shutdown: " + error); }
            };
            if (InitialPaths.FirstOrDefault() is { } path) _window!.Open(path);
            else _ = _window!.RestoreLastAsync();
        }
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
        {
            activation.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) OpenWindow();
                if (e is FileActivatedEventArgs files) foreach (var item in files.Files) if (item.TryGetLocalPath() is { } path) OpenPath(path);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    /// <summary>无窗口时重建窗口；首版单窗口入口。</summary>
    public void OpenWindow(bool restore = true)
    {
        if (_window is not null) { _window.Activate(); return; }
        _window = ActivatorUtilities.CreateInstance<MainWindow>(Services);
        _window.Closed += (_, _) => _window = null;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = _window;
        _window.Show();
        if (restore) _ = _window.RestoreLastAsync();
    }
    /// <summary>统一处理系统打开事件，支持启动完成前排队。</summary>
    public void OpenPath(string path)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { OpenWindow(false); _window!.Open(path); });
    }
}
