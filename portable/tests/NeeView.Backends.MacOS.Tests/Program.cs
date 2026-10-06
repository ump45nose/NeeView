namespace NeeView.Backends.MacOS.Tests;
/// <summary>无窗口原生测试宿主；主线程泵送系统排队回调，后台进入同一个xUnit runner。</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _ = AppKit.NSApplication.SharedApplication;
        var run = Task.Run(() => Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args));
        // 仅泵送Foundation/AppKit回调，不创建窗口、不激活应用；Info.plist保持LSBackgroundOnly。
        while (!run.IsCompleted)
        {
            using var until = Foundation.NSDate.FromTimeIntervalSinceNow(.01);
            Foundation.NSRunLoop.Main.RunUntil(until);
        }
        return run.GetAwaiter().GetResult();
    }
}
