using Avalonia;

namespace NeeView.MacOS;

public static class Program
{
    /// <summary>正式 ARM64 Host，装配 AppKit 适配并由 Avalonia 管理原生事件。</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // 将框架警告与异步启动错误写入标准错误，便于正式 Host 的本机验收留证。
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Error));
        System.Diagnostics.Trace.AutoFlush = true;
        var request = NeeView.ScriptLaunchRequest.Parse(args);
        MacApp.InitialPaths = request.Paths; MacApp.InitialScript = request.Script;
        return AppBuilder.Configure<MacApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
