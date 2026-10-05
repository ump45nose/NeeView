// Copyright (c) NeeLaboratory. 原 ArchivePolicy/ArchiveEntryUtility 实体化策略，基线 c5c398d89。
namespace NeeView;

/// <summary>原归档复制策略；数值顺序与原 JSON 一致。</summary>
public enum ArchivePolicy { None, SendArchiveFile, SendArchivePath, SendExtractFile }

public static class ArchivePolicyExtensions
{
    /// <summary>真实文件复制不能使用虚拟路径，沿原实现降为解压文件。</summary>
    /// <param name="policy">原系统配置策略。</param><returns>适用于实体复制的策略。</returns>
    public static ArchivePolicy LimitedRealization(this ArchivePolicy policy) => policy == ArchivePolicy.SendArchivePath ? ArchivePolicy.SendExtractFile : policy;
}

/// <summary>替换原 GetFileProxyAsync 的临时实体后端，逻辑定位不被临时路径改写。</summary>
public interface IArchiveEntryRealizer
{
    /// <summary>提取一个文件条目；目录、链接及超出预算必须明确失败。</summary>
    /// <param name="entry">仍由当前书籍拥有的原条目。</param><param name="remainingBytes">本批剩余临时字节预算。</param>
    /// <param name="token">准备阶段取消。</param><returns>调用方拥有的独立临时文件租约。</returns>
    Task<RealizedFileLease> ExtractAsync(ArchiveEntry entry, long remainingBytes, CancellationToken token);
    /// <summary>成功写剪贴板后转交整批所有权；替换旧批，关窗或切书不释放。</summary>
    /// <param name="files">已写入系统剪贴板的整批文件；即使没有临时文件也替换旧批。</param>
    /// <returns>转交完成任务；清理失败保留旧材料并由进程退出重试。</returns>
    Task RetainClipboardAsync(RealizedFilePathList files);
}

/// <summary>单个临时实体的所有权；普通文件引用无需此租约。</summary>
public sealed class RealizedFileLease(string path, long length, Func<ValueTask> release) : IAsyncDisposable
{
    private Func<ValueTask>? _release = release;
    public string Path { get; } = path;
    public long Length { get; } = length;
    /// <summary>只释放租约生成的材料，不删除源文件；释放成功后才能清除回调。</summary>
    public async ValueTask DisposeAsync()
    { if (_release is { } action) { await action(); _release = null; } }
}

/// <summary>原保序去重的实体路径与临时租约集合，准备失败时整批释放。</summary>
public sealed class RealizedFilePathList : IAsyncDisposable
{
    public const long TemporaryByteBudget = 2L * 1024 * 1024 * 1024;
    private readonly List<RealizedFileLease> _leases = [];
    private readonly List<string> _paths = [];
    private readonly List<string> _unrealizedDirectories = [];
    public IReadOnlyList<string> Paths => _paths;
    /// <summary>原内部目录提取返回null的条目；向表现回报未完成能力，不把空批次当作成功复制。</summary>
    public IReadOnlyList<string> UnrealizedDirectories => _unrealizedDirectories;
    public string? CapabilityWarning => _unrealizedDirectories.Count == 0 ? null : "归档内部目录未提取（原版尚未实现），其他可复制条目按原策略处理：" + string.Join("、", _unrealizedDirectories);
    /// <summary>记录原策略跳过的逻辑目录，不生成临时文件或改变条目定位。</summary>
    /// <param name="name">原条目逻辑名称。</param>
    internal void MarkUnrealizedDirectory(string name) { if (!_unrealizedDirectories.Contains(name, StringComparer.Ordinal)) _unrealizedDirectories.Add(name); }
    public long TemporaryBytes => _leases.Sum(lease => lease.Length);
    /// <summary>登记已生成的材料，按原完整路径去重；不根据文件名误合并不同条目。</summary>
    /// <param name="path">真实或原策略指定的逻辑路径。</param><param name="lease">仅解压输出持有租约。</param>
    internal void Add(string path, RealizedFileLease? lease = null)
    { if (lease is not null) _leases.Add(lease); if (!_paths.Contains(path, StringComparer.Ordinal)) _paths.Add(path); }
    /// <summary>释放所有临时材料；一项失败不阻止其他项清理，失败项保留供重试。</summary>
    public async ValueTask DisposeAsync()
    {
        List<Exception> errors = [];
        foreach (var lease in _leases.ToArray())
        { try { await lease.DisposeAsync(); _leases.Remove(lease); } catch (Exception ex) { errors.Add(ex); } }
        if (errors.Count > 0) throw new AggregateException("临时实体清理失败。", errors);
    }
}

/// <summary>原 ArchiveEntryUtility.RealizeArchiveEntry 的保序/策略/去重链路。</summary>
public static class ArchiveEntryUtility
{
    /// <summary>真实目录文件保持原地址；归档按原四策略解析，不将缓存当作原页面路径。</summary>
    /// <param name="entries">已按原页组收集的条目。</param><param name="policy">剪贴板使用原值，固定复制先 LimitedRealization。</param>
    /// <param name="realizer">只在提取归档文件时需要的后端。</param><param name="token">整个准备过程取消。</param>
    /// <returns>调用方拥有的保序路径与临时资源批次。</returns>
    public static async Task<RealizedFilePathList> RealizeArchiveEntry(IEnumerable<ArchiveEntry> entries, ArchivePolicy policy, IArchiveEntryRealizer? realizer, CancellationToken token)
    {
        if (!Enum.IsDefined(policy)) throw new NotSupportedException("未知的原归档复制策略。");
        var files = new RealizedFilePathList();
        try
        {
            foreach (var entry in entries.Select(entry => entry.TargetArchiveEntry).Distinct())
            {
                token.ThrowIfCancellationRequested();
                if (entry.Archive.IsDisposed || entry.IsShortcut) throw new NotSupportedException("此条目尚不支持复制：" + entry.EntryName);
                if (entry.FilePath is { } path) files.Add(path);
                else switch (policy)
                {
                    case ArchivePolicy.None: break;
                    case ArchivePolicy.SendArchiveFile: files.Add(entry.Archive.RootArchivePath); break;
                    case ArchivePolicy.SendArchivePath: files.Add(entry.SystemPath); break;
                    case ArchivePolicy.SendExtractFile:
                        // 原ArchiveEntry.RealizeAsync内部目录分支返回null，其他策略仍可传根归档/虚拟路径。
                        if (entry.IsDirectory) { files.MarkUnrealizedDirectory(entry.EntryName); break; }
                        if (realizer is null) throw new NotSupportedException("归档实体化后端尚未装配。");
                        var lease = await realizer.ExtractAsync(entry, RealizedFilePathList.TemporaryByteBudget - files.TemporaryBytes, token);
                        files.Add(lease.Path, lease); break;
                }
            }
            token.ThrowIfCancellationRequested(); return files;
        }
        catch { await files.DisposeAsync(); throw; }
    }
}
