namespace NeeView.Backends.MacOS.Tests;
/// <summary>官方原生测试宿主不运行AppKit事件循环；清除原生同步上下文后进入同一个xUnit runner。</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
