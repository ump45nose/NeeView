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
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    /// <summary>创建首个窗口并将启动/Finder 事件送入统一打开流程。</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            OpenWindow(); desktop.MainWindow = _window;
            desktop.ShutdownRequested += async (_, e) =>
            {
                e.Cancel = true;
                if (_window is not null) await _window.FlushAsync();
                if (ShutdownServices is not null) await ShutdownServices(); desktop.Shutdown();
            };
            if (InitialPaths.FirstOrDefault() is { } path) _window!.Open(path);
            else _window!.RestoreLast();
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
    public void OpenWindow()
    {
        if (_window is not null) { _window.Activate(); return; }
        _window = ActivatorUtilities.CreateInstance<MainWindow>(Services);
        _window.Closed += (_, _) => _window = null; _window.Show();
    }
    /// <summary>统一处理系统打开事件，支持启动完成前排队。</summary>
    public void OpenPath(string path)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { OpenWindow(); _window!.Open(path); });
    }
}
