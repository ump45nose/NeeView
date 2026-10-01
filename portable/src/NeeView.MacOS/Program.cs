using Avalonia;
using NeeView.Desktop;
using NeeView.Host;
using NeeView.Platform.MacOS;

namespace NeeView.MacOS;

public static class Program
{
    /// <summary>正式 ARM64 Host，装配 AppKit 适配并由 Avalonia 管理原生事件。</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Composition.CreateAsync(new MacPlatformService()).GetAwaiter().GetResult(); DesktopApp.InitialPaths = args;
        return AppBuilder.Configure<DesktopApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
