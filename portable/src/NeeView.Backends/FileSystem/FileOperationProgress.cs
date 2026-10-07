namespace NeeView.Backends;

/// <summary>请求作用域中的真实I/O进展；不通过周期心跳掩盖卡住的系统调用。</summary>
internal static class FileOperationProgress
{
    private static readonly AsyncLocal<Action?> Current = new();
    public static void Report() => Current.Value?.Invoke();
    public static IDisposable Observe(Action report)
    { var old = Current.Value; Current.Value = report; return new Scope(() => Current.Value = old); }
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
