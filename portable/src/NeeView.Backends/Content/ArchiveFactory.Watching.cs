namespace NeeView.Backends;
public sealed partial class ArchiveFactory
{
    /// <summary>替换原Windows系统目录事件，只监视直接子目录，目录树控制数量/防抖/生命周期。</summary>
    public IDisposable? WatchDirectory(string path, Action changed)
    {
        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.DirectoryName };
        watcher.Created += (_, _) => changed(); watcher.Deleted += (_, _) => changed(); watcher.Renamed += (_, _) => changed(); watcher.Error += (_, _) => changed();
        try { watcher.EnableRaisingEvents = true; return watcher; } catch { watcher.Dispose(); throw; }
    }
}
