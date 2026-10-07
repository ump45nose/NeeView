// Copyright (c) NeeLaboratory.
namespace NeeView;

/// <summary>一次原脚本调用的业务与线程边界；具体窗口仅通过命令委托接入。</summary>
public sealed class ScriptAccessContext
{
    public required BookOperation Operation { get; init; }
    public required SaveData State { get; init; }
    public required CommandTable Commands { get; init; }
    public required IPropertyMapDispatcher Dispatcher { get; init; }
    public required Func<Func<Task>, Task> InvokeAsync { get; init; }
    public required Func<string, object?[], Task<bool>> ExecuteCommandAsync { get; init; }
    public required Func<string, bool> CanExecute { get; init; }
    public CancellationToken Token { get; init; }
    public IAccessDiagnostics Diagnostics { get; init; } = new DefaultAccessDiagnostics();
    public Func<CancellationToken, Task> WaitForDisplayAsync { get; init; } = _ => Task.CompletedTask;
    public Func<Page, IMediaPlayer?> MediaPlayer { get; init; } = _ => null;
    public Action? RefreshPresentation { get; init; }
    /// <summary>在实际界面线程读取当前原模型；脚本不直接访问可变集合。</summary>
    public T Read<T>(Func<T> action) { Token.ThrowIfCancellationRequested(); return Dispatcher.Invoke(action); }
    /// <summary>属性写入回到界面线程，再请求同一显示链刷新。</summary>
    public void Write(Action action) { Token.ThrowIfCancellationRequested(); Dispatcher.Invoke(() => { action(); RefreshPresentation?.Invoke(); }); }
    /// <summary>原集合增删返回实际结果，并与属性写入使用同一调度边界。</summary>
    public T Write<T>(Func<T> action)
    {
        Token.ThrowIfCancellationRequested();
        return Dispatcher.Invoke(() => { var result = action(); RefreshPresentation?.Invoke(); return result; });
    }
    /// <summary>后台JavaScript同步等待异步业务完成；不阻塞UI线程，不在取消后继续排队。</summary>
    public void Run(Func<Task> action)
    {
        Token.ThrowIfCancellationRequested();
        if (Dispatcher.CheckAccess()) throw new InvalidOperationException("同步脚本只能在后台运行。");
        InvokeAsync(async () => { Token.ThrowIfCancellationRequested(); await action(); }).GetAwaiter().GetResult();
    }
}
