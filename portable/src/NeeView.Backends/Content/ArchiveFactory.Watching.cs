namespace NeeView.Backends;
public sealed partial class ArchiveFactory
{
    /// <summary>原搜索结果文件/目录监视替换点，后台事件只回报刷新资格，不持有窗口。</summary>
    /// <param name="path">真实普通目录。</param><param name="recursive">是否监视子目录。</param><param name="changed">来源变更回报。</param>
    /// <returns>调用方释放的监视；归档逻辑地址不支持时为空。</returns>
    public IDisposable? WatchBookSearch(string path, bool recursive, Action changed)
    {
        // 归档/内部逻辑路径没有可监视的目录，明确使用手动刷新，不伪造系统事件。
        if (!Directory.Exists(path)) return null;
        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Created += (_, _) => changed(); watcher.Deleted += (_, _) => changed(); watcher.Renamed += (_, _) => changed(); watcher.Changed += (_, _) => changed(); watcher.Error += (_, _) => changed();
        try { watcher.EnableRaisingEvents = true; return watcher; } catch { watcher.Dispose(); throw; }
    }
    /// <summary>替换原Windows系统目录事件，只监视直接子目录，目录树控制数量/防抖/生命周期。</summary>
    public IDisposable? WatchDirectory(string path, Action changed)
    {
        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.DirectoryName };
        watcher.Created += (_, _) => changed(); watcher.Deleted += (_, _) => changed(); watcher.Renamed += (_, _) => changed(); watcher.Error += (_, _) => changed();
        try { watcher.EnableRaisingEvents = true; return watcher; } catch { watcher.Dispose(); throw; }
    }
}
