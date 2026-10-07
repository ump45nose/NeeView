using System.Text;
using Jint;
using JsEngine = Jint.Engine;

namespace NeeView.Backends;

/// <summary>基于 Jint 的宿主无关脚本运行时工厂。</summary>
public sealed class JintScriptRuntimeFactory : IScriptRuntimeFactory
{
    public IScriptRuntime Create(Action<object?> log, Action<string, string?> system, CancellationToken token) =>
        new JintScriptRuntime(log, system, token);
}

internal sealed class JintScriptRuntime : IScriptRuntime
{
    private JsEngine? _engine;
    private readonly Action<object?> _log;
    private readonly Action<string, string?> _system;
    private readonly CancellationToken _token;
    private string? _directory;
    private bool _disposed;

    public JintScriptRuntime(Action<object?> log, Action<string, string?> system, CancellationToken token)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _token = token;
        _engine = new JsEngine(options => options.CancellationToken(token).DebugMode(true)
            .SetTypeConverter(e => new JintCustomTypeConverter(e)).AddObjectConverter(new JintCustomObjectConverter())
            .AllowClr(typeof(System.Diagnostics.Process).Assembly));
        // object重载仍由Jint包装真实委托；不要求裁剪分析保留System.Delegate的全部反射工厂。
        _engine.SetValue("sleep", (object)(Action<int>)Sleep);
        _engine.SetValue("log", (object)(Action<object?>)Log);
        _engine.SetValue("system", (object)(Action<string, string?>)SystemCall);
        _engine.SetValue("include", (object)(Func<string, object?>)ExecuteFile);
    }

    public string? ScriptPath { get; private set; }
    public string? ScriptDirectory => _directory;
    private JsEngine Runtime => _engine ?? throw new ObjectDisposedException(nameof(JintScriptRuntime));

    public object? Evaluate(string script, string? path = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _token.ThrowIfCancellationRequested();
        var oldPath = ScriptPath;
        try { ScriptPath = path; return (path is null ? Runtime.Evaluate(script) : Runtime.Evaluate(script, path)).ToObject(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (_token.IsCancellationRequested) { throw new OperationCanceledException(_token); }
        catch (Exception ex) { throw Wrap(ex, path ?? oldPath); }
        finally { ScriptPath = oldPath; }
    }

    public object? ExecuteFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(Path.IsPathFullyQualified(path) || _directory is null ? path : Path.Combine(_directory, path));
        var old = _directory;
        try { _directory = Path.GetDirectoryName(full); return Evaluate(File.ReadAllText(full, Encoding.UTF8), full); }
        finally { _directory = old; }
    }

    public void SetValue(string name, object? value) { _token.ThrowIfCancellationRequested(); Runtime.SetValue(name, value); }
    public object? GetValue(string name) { _token.ThrowIfCancellationRequested(); return Runtime.GetValue(name).ToObject(); }
    private void Log(object? value) { _token.ThrowIfCancellationRequested(); _log(value); }
    private void SystemCall(string file, string? args) { _token.ThrowIfCancellationRequested(); _system(file, args); }
    private void Sleep(int milliseconds) { _token.WaitHandle.WaitOne(milliseconds); _token.ThrowIfCancellationRequested(); }
    /// <summary>保留最深include错误来源与真实JS行号；纯宿主错误回退到当前语句位置。</summary>
    private Exception Wrap(Exception ex, string? path)
    {
        if (ex is ScriptExecutionException) return ex;
        if (ex is Acornima.ParseErrorException { Error: { } error })
            return new ScriptExecutionException(error.Description, error.SourceFile ?? path, error.LineNumber, ex);
        if (JintException.TryGetJavaScriptLocation(ex, out var location))
            return new ScriptExecutionException(ex.Message, location.SourceFile ?? path, location.Start.Line, ex);
        var current = Runtime.Debugger.CurrentLocation;
        return new ScriptExecutionException(ex.Message, current?.SourceFile ?? path, current?.Start.Line ?? -1, ex);
    }
    /// <summary>执行任务结束后释放该引擎的宿主引用，不清空进程级nv.Values。</summary>
    public void Dispose() { _disposed = true; _engine = null; }
}
