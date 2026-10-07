// Copyright (c) NeeLaboratory. 原脚本目录监视，MIT。
namespace NeeView;
/// <summary>监视顶层脚本修改，合并一秒内通知；不持有窗口或脚本运行时。</summary>
public sealed class ScriptFolderWatcher : IDisposable
{
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;
    private int _version;
    public event EventHandler? Changed;
    public void Start(string? path)
    {
        lock (_gate)
        {
            if (_watcher?.Path == path) return;
            StopCore();
            if (_disposed || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try
            {
                var watcher = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                watcher.Created += OnChanged; watcher.Changed += OnChanged; watcher.Deleted += OnChanged; watcher.Renamed += OnChanged;
                watcher.Error += OnError; _watcher = watcher; watcher.EnableRaisingEvents = true;
            }
            catch (IOException) { StopCore(); }
            catch (UnauthorizedAccessException) { StopCore(); }
        }
    }
    public void Stop() { lock (_gate) StopCore(); }
    private void StopCore() { ++_version; _watcher?.Dispose(); _watcher = null; _timer?.Dispose(); _timer = null; }
    private void OnChanged(object? sender, FileSystemEventArgs e)
    {
        if (!Path.GetExtension(e.Name ?? "").Equals(".nvjs", StringComparison.OrdinalIgnoreCase)
            && !(e is RenamedEventArgs rename && Path.GetExtension(rename.OldName ?? "").Equals(".nvjs", StringComparison.OrdinalIgnoreCase))) return;
        Debounce();
    }
    private void OnError(object? sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher)) return;
            var path = _watcher!.Path;
            // Error可能表示缓冲溢出或监听已停止；强制重建，不触发同路径短路。
            StopCore(); Start(path);
        }
        Debounce();
    }
    private void Debounce()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var version = ++_version; _timer?.Dispose();
            _timer = new Timer(_ =>
            {
                lock (_gate) { if (_disposed || version != _version) return; }
                Changed?.Invoke(this, EventArgs.Empty);
            }, null, 1000, Timeout.Infinite);
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; StopCore(); } }
}
