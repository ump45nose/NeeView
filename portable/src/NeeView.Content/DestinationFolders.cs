using NeeView.Application;

namespace NeeView.Content;

public sealed class FolderNavigator : IFolderNavigator
{
    /// <summary>后台列出直接子目录，用于树节点的延迟展开。</summary>
    public async Task<IReadOnlyList<string>> ChildrenAsync(string directory, CancellationToken token = default) =>
        await SourceIo.RunAsync(() => Directory.EnumerateDirectories(directory).OrderBy(p => Path.GetFileName(p), NeeView.Core.NaturalNameComparer.Instance).ToArray(), token);
}

/// <summary>目标目录双区服务；直接子目录仅在切目录或手动刷新时枚举。</summary>
public sealed class DestinationFolderService(ISettingsStore settings) : IDestinationFolderService
{
    private string? _directory;
    private long _version;
    public IReadOnlyList<string> Managed { get; private set; } = [];
    public IReadOnlyList<string> Children { get; private set; } = [];
    /// <summary>刷新配置及当前目录；晚到结果须匹配请求代次。</summary>
    public async Task RefreshAsync(string? directory, bool force, CancellationToken token = default)
    {
        var configuration = await settings.LoadAsync(token); Managed = configuration.DestinationFolders.ToArray();
        if (!force && (_directory == directory || !configuration.AutoRefreshDestinations)) return;
        _directory = directory; var version = Interlocked.Increment(ref _version);
        if (directory is null) { Children = []; return; }
        var children = await SourceIo.RunAsync(() => Directory.EnumerateDirectories(directory)
            .OrderBy(p => Path.GetFileName(p), NeeView.Core.NaturalNameComparer.Instance).ToArray(), token);
        if (version == _version && directory == _directory) Children = children;
    }
    /// <summary>新建当前目录的直接子目录；拒绝越界路径，不套用 Windows 保留名称规则。</summary>
    public async Task<string> CreateChildAsync(string directory, string name, CancellationToken token = default)
    {
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny([Path.DirectorySeparatorChar, '\0']) >= 0)
            throw new ArgumentException("请输入直接子目录名。");
        var child = Path.GetFullPath(Path.Combine(directory, name));
        if (Path.GetDirectoryName(child) != Path.GetFullPath(directory)) throw new ArgumentException("目录不能越出当前路径。");
        await SourceIo.RunAsync(() => Directory.CreateDirectory(child).FullName, token);
        await RefreshAsync(directory, true, token); return child;
    }
}
