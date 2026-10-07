// Copyright (c) NeeLaboratory. 原ScriptManager/UnitPool的执行、事件与文件监视关系，MIT。
using System.Collections.Concurrent;
using System.ComponentModel;
namespace NeeView;

/// <summary>每次执行的独立引擎、原命令与参数；Values属于唯一装配的进程服务。</summary>
public sealed record ScriptInvocation(IScriptRuntime Runtime, string? CommandName, object?[] Args,
    ConcurrentDictionary<string, object?> Values, CancellationToken Token);
public sealed record ScriptLog(string Level, string Message, string? Path = null, int Line = -1);

/// <summary>原脚本发现与运行协调；UI装配提供nv对象和系统启动，执行从不占用UI线程。</summary>
public sealed class ScriptManager : IAsyncDisposable
{
    private readonly IScriptRuntimeFactory _factory;
    private readonly Func<ScriptInvocation, object> _createHost;
    private readonly Action<string, string?> _system;
    private readonly ScriptConfig _config;
    private readonly ScriptFolderWatcher _watcher = new();
    private readonly object _gate = new();
    private readonly HashSet<ScriptUnit> _units = [];
    private readonly ConcurrentDictionary<string, object?> _values;
    private IReadOnlyList<ScriptCommandSource> _sources = Array.Empty<ScriptCommandSource>();
    private int _version;
    private bool _disposed;
    private Task? _disposeTask;
    public IReadOnlyList<ScriptCommandSource> Sources => Volatile.Read(ref _sources);
    public int ActiveCount { get { lock (_gate) return _units.Count; } }
    public event EventHandler? SourcesChanged;
    public event EventHandler<ScriptLog>? Log;
    /// <summary>调用方持有本服务并在窗口重建时注入同一Values；显式执行不依赖目录开关。</summary>
    public ScriptManager(IScriptRuntimeFactory factory, Func<ScriptInvocation, object> createHost,
        Action<string, string?> system, ScriptConfig config, ConcurrentDictionary<string, object?>? values = null)
    {
        _factory = factory; _createHost = createHost; _system = system; _config = config;
        _values = values ?? new();
        _watcher.Changed += WatcherChanged; _config.PropertyChanged += ConfigChanged;
        UpdateWatcher();
    }
    private void WatcherChanged(object? sender, EventArgs e) => _ = ReloadObservedAsync();
    private void ConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ScriptConfig.IsScriptFolderEnabled) or nameof(ScriptConfig.ScriptFolder))) return;
        UpdateWatcher(); _ = ReloadObservedAsync();
    }
    private void UpdateWatcher()
    { lock (_gate) { if (_disposed) return; if (_config.IsScriptFolderEnabled) _watcher.Start(_config.ScriptFolder); else _watcher.Stop(); } }
    private async Task ReloadObservedAsync()
    { try { await ReloadAsync(); } catch (OperationCanceledException) { } catch (Exception ex) { Emit(new("error", ex.Message)); } }
    /// <summary>只扫描顶层nvjs；后台生成快照，晚到扫描不得覆盖新目录或已关闭服务。</summary>
    public async Task ReloadAsync(CancellationToken token = default)
    {
        UpdateWatcher();
        int version; bool enabled; string folder;
        lock (_gate) { if (_disposed) return; version = ++_version; enabled = _config.IsScriptFolderEnabled; folder = _config.ScriptFolder; }
        var list = await Task.Run(() =>
        {
            var result = new List<ScriptCommandSource>(); token.ThrowIfCancellationRequested();
            if (!enabled || !Directory.Exists(folder)) return result;
            foreach (var path in Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).Equals(".nvjs", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                try { result.Add(ScriptCommandSource.Create(path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Emit(new("error", ex.Message, path)); }
            }
            return result;
        }, token).ConfigureAwait(false);
        lock (_gate) { if (_disposed || version != _version) return; Volatile.Write(ref _sources, list.AsReadOnly()); }
        SourcesChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>以独立运行时执行UTF-8文件；include仍使用该运行时。</summary>
    public Task<object?> RunFileAsync(string path, object?[]? args = null, CancellationToken token = default, string? commandName = null) =>
        Start(commandName, args ?? [], runtime => runtime.ExecuteFile(path), path, token);
    public Task<object?> EvaluateAsync(string code, CancellationToken token = default) => Start(null, [], r => r.Evaluate(code), null, token);
    /// <summary>关闭目录开关时不运行事件；五文件名按原大小写匹配。</summary>
    public Task<object?> RunEventAsync(string name, object?[]? args = null, CancellationToken token = default)
    {
        if (!_config.IsScriptFolderEnabled) return Task.FromResult<object?>(null);
        var source = Sources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.Ordinal));
        return source is null ? Task.FromResult<object?>(null) : RunFileAsync(source.Path, args, token, "Script_" + source.Name);
    }
    private Task<object?> Start(string? command, object?[] args, Func<IScriptRuntime, object?> execute, string? path, CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var unit = new ScriptUnit(token); _units.Add(unit);
            unit.Start(ct =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var runtime = _factory.Create(value => Emit(new("log", Convert.ToString(value) ?? "", path)), _system, ct);
                    runtime.SetValue("nv", _createHost(new(runtime, command, args.ToArray(), _values, ct)));
                    return execute(runtime);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = ex as ScriptExecutionException ?? new ScriptExecutionException(ex.Message, path, -1, ex);
                    Emit(new("error", error.Message, error.SourcePath, error.Line)); throw error;
                }
            });
            // 从池移除只能发生在任务(含运行时和CTS释放)真正结束之后。
            _ = unit.Task.ContinueWith(_ => { lock (_gate) _units.Remove(unit); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return unit.Task;
        }
    }
    /// <summary>原旧成员诊断使用同一有界控制台日志链。</summary>
    public void ReportNotice(ScriptLog entry) { lock (_gate) { if (_disposed) return; } Emit(entry); }
    private void Emit(ScriptLog entry) => Log?.Invoke(this, entry);
    /// <summary>取消所有排队/运行脚本；原生宿主调用等待完成后释放，不产生成功结果。</summary>
    public void CancelAll() { lock (_gate) foreach (var unit in _units) unit.Cancel(); }
    /// <summary>关闭准备时取消并等待当前调用，保留服务供保存失败后的同一窗口重试。</summary>
    public Task CancelAndWaitAsync()
    {
        lock (_gate) return WaitForUnitsAsync(_units.Select(u => { u.Cancel(); return u.Task; }).ToArray());
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true; ++_version;
            _config.PropertyChanged -= ConfigChanged; _watcher.Changed -= WatcherChanged; _watcher.Dispose();
            var tasks = _units.Select(u => { u.Cancel(); return u.Task; }).ToArray();
            return new(_disposeTask = WaitForUnitsAsync(tasks));
        }
    }
    private static async Task WaitForUnitsAsync(Task<object?>[] tasks)
    { try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch (Exception) { /* 运行者收到错误；释放继续等待全部任务。 */ } }
}
