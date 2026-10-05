namespace NeeView;

/// <summary>替换原Shell操作的实体契约；目录仅固定复制且必须携带既有后端的确认快照。</summary>
public sealed record FileTransferRequest(string Source, string Destination, bool Move, bool Overwrite = false,
    string? RestoreBackup = null, string? ExpectedSourceHash = null, string? ExpectedRestoreHash = null, BookTransferPlan? DirectoryCopyPlan = null, bool PreserveSourceLink = false);
/// <summary>真实落点和可恢复覆盖副本；反向操作可消费副本恢复被覆盖文件。</summary>
public sealed record FileTransferResult(string Source, string Destination, bool Move, string? Backup, string Journal, string ContentHash, string? BackupHash);
public interface IFileOperationBackend
{
    Task<bool> FileExistsAsync(string path, CancellationToken token);
    /// <summary>临时目标、完整性检查和恢复日志完成后才返回；失败不能报告成功。</summary>
    Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token);
    /// <summary>清理应用自己创建的已完成记录和覆盖副本，不清理用户目录。</summary>
    Task ReleaseAsync(FileTransferResult result);
    /// <summary>启动时保守回滚未完成操作；不能可靠判定的记录保留并返回提示。</summary>
    Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default);
    /// <summary>在指定目录下创建一个直接子目录；不得覆盖或跨出父目录。</summary>
    Task CreateDirectoryAsync(string parent, string name, CancellationToken token);
}
