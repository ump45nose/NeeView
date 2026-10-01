using System.Security.Cryptography;
using NeeView.Core;

namespace NeeView.Application;

/// <summary>进程共享文件操作状态机；提交点前可取消，提交点后保留日志并完成文件和身份更新。</summary>
public sealed class FileActionService(IIdentityRegistry identities, IReaderStateStore states, IPlatformService platform, string backupRoot) : IFileActionService
{
    private sealed record MoveRecord(string Source, string Target, string Digest, string? Backup, string? BackupDigest);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<MoveRecord> _undo = [];
    private readonly List<MoveRecord> _redo = [];
    private int _capacity = 300;
    public bool CanUndo => _undo.Count > 0 && _gate.CurrentCount > 0;
    public bool CanRedo => _redo.Count > 0 && _gate.CurrentCount > 0;
    public int Capacity
    {
        get => _capacity;
        set { _capacity = Math.Clamp(value, 0, 1000); Trim(_undo); Trim(_redo); }
    }
    /// <summary>输入历史栈，裁剪最旧记录；日志可能依赖的备份不能提前删除。</summary>
    private void Trim(List<MoveRecord> history) { while (history.Count > _capacity) history.RemoveAt(0); }
    /// <summary>输入真实目录图片、动作及目标；版本不一致时拒绝操作，返回实际提交结果。</summary>
    public Task<FileActionResult> ExecuteAsync(PageDescriptor page, FileActionKind action, string? target,
        ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
        => Task.Run(() => ExecuteCoreAsync(page, action, target, conflict, token), CancellationToken.None);
    /// <summary>后台执行文件元数据和文件系统操作，NAS 不阻塞 UI 线程。</summary>
    private async Task<FileActionResult> ExecuteCoreAsync(PageDescriptor page, FileActionKind action, string? target, ConflictChoice conflict, CancellationToken token)
    {
        if (page.Locator.Entry is not null) return new(false, page.Locator.Path, null, "压缩包内部条目只读。");
        await _gate.WaitAsync(token);
        try
        {
            await EnsureNoPendingAsync(token);
            var file = new FileInfo(page.Locator.Path);
            if (!file.Exists || new ContentVersion(file.Length, file.LastWriteTimeUtc.Ticks) != page.Version)
                return new(false, page.Locator.Path, null, "图片已被外部修改，请刷新后重试。");
            if (action == FileActionKind.Trash)
            {
                await platform.TrashAsync(page.Locator.Path, token);
                return new(true, page.Locator.Path, null);
            }
            if (target is null) return new(false, page.Locator.Path, null, "未指定目标。");
            var destination = action == FileActionKind.Rename ? Path.GetFullPath(target) : Path.Combine(Path.GetFullPath(target), file.Name);
            var (result, operation) = await TransferAsync(file.FullName, destination, action == FileActionKind.Copy, conflict, null, null, null, token);
            if (result.Success && action != FileActionKind.Copy)
            {
                _redo.Clear();
                if (_capacity > 0) { _undo.Add(new(file.FullName, destination, operation!.SourceDigest!, operation.Backup, operation.BackupDigest)); Trim(_undo); }
            }
            return result;
        }
        catch (Exception error) { return new(false, page.Locator.Path, target, ReaderException.From(error).Message); }
        finally { _gate.Release(); }
    }
    /// <summary>有未完成操作时禁止叠加新写入，避免同一路径被两个恢复记录争用。</summary>
    private async Task EnsureNoPendingAsync(CancellationToken token)
    {
        if ((await states.RecoveriesAsync(token)).Count > 0) throw new IOException("存在待恢复文件操作，请先恢复或核对报告中的路径。");
    }
    /// <summary>输入移动/复制路径及附加恢复备份；写完整日志后执行，任何提交后失败均由日志续做。</summary>
    private async Task<(FileActionResult Result, RecoveryOperation? Operation)> TransferAsync(string source, string target, bool copy,
        ConflictChoice conflict, string? expectedDigest, string? restoreBackup, string? restoreDigest, CancellationToken token)
    {
        source = Path.GetFullPath(source); target = Path.GetFullPath(target);
        if (source == target) return (new(false, source, target, "源和目标相同。"), null);
        if (!File.Exists(source)) return (new(false, source, target, "源文件不存在。"), null);
        if (Directory.Exists(target) || !Directory.Exists(Path.GetDirectoryName(target))) return (new(false, source, target, "目标目录不存在或目标是目录。"), null);
        if (File.Exists(target) && conflict != ConflictChoice.Overwrite) return (new(false, source, target, "目标同名文件已存在，请选择覆盖或取消。"), null);
        var digest = await DigestAsync(source, token);
        if (expectedDigest is not null && digest != expectedDigest) throw new IOException("移动记录的图片已被外部修改，拒绝撤销或重做。");
        if (restoreBackup is not null) await RequireDigestAsync(restoreBackup, restoreDigest, token);
        var id = Guid.NewGuid().ToString("N");
        var backup = File.Exists(target) ? Path.Combine(backupRoot, id + ".backup") : null;
        var backupDigest = backup is null ? null : await DigestAsync(target, token);
        var operation = new RecoveryOperation(id, source, target, Path.Combine(Path.GetDirectoryName(target)!, ".neeview-" + id + ".tmp"),
            backup, "preparing", copy, digest, backupDigest, await identities.GetContentAsync(new(source), token),
            restoreBackup, restoreBackup is null ? null : source, restoreDigest);
        await states.RecordOperationAsync(operation, token);
        try
        {
            if (backup is not null) { Directory.CreateDirectory(backupRoot); await CopyVerifiedAsync(target, backup, backupDigest!, token); }
            await CopyVerifiedAsync(source, operation.Temporary!, digest, token);
            await RequireDigestAsync(source, digest, token);
            if (backup is not null) await RequireDigestAsync(target, backupDigest, token);
            else if (File.Exists(target)) throw new IOException("目标在复制期间出现了同名文件，已取消覆盖。");
            token.ThrowIfCancellationRequested();
            // 先持久化意图，再原子提交临时目标；恢复器用摘要区分提交前后。
            operation = operation with { Stage = "ready" }; await states.RecordOperationAsync(operation, token);
            await CompleteAsync(operation);
            return (new(true, source, target, Content: copy ? await identities.GetContentAsync(new(target), default) : operation.Content), operation);
        }
        catch
        {
            // ready 之后不能盲目回滚：身份可能已提交；保留日志，由同一状态机续做。
            if (operation.Stage == "preparing")
            {
                if (File.Exists(operation.Temporary)) File.Delete(operation.Temporary);
                if (backup is not null && File.Exists(backup)) File.Delete(backup);
                await states.RemoveOperationAsync(id);
            }
            throw;
        }
    }
    /// <summary>按摘要重放已准备操作；不删除或覆盖任何不符合预期的外部内容。</summary>
    private async Task CompleteAsync(RecoveryOperation operation)
    {
        var token = CancellationToken.None;
        if (operation.SourceDigest is null) throw new IOException("旧恢复记录缺少摘要，需要手动核对源、目标和备份。");
        if (operation.Stage is "ready" or "target-committed")
        {
            if (!await MatchesAsync(operation.Target, operation.SourceDigest, token))
            {
                await RequireDigestAsync(operation.Temporary, operation.SourceDigest, token);
                if (operation.Backup is not null)
                {
                    await RequireDigestAsync(operation.Backup, operation.BackupDigest, token);
                    await RequireDigestAsync(operation.Target, operation.BackupDigest, token);
                }
                else if (File.Exists(operation.Target)) throw new IOException("恢复目标已有外部文件，拒绝覆盖。");
                File.Move(operation.Temporary!, operation.Target, operation.Backup is not null);
            }
            operation = operation with { Stage = "target-committed" }; await states.RecordOperationAsync(operation);
            if (!operation.Copy && File.Exists(operation.Source))
            {
                await RequireDigestAsync(operation.Source, operation.SourceDigest, token);
                File.Delete(operation.Source);
            }
            operation = operation with { Stage = "source-deleted" }; await states.RecordOperationAsync(operation);
        }
        if (operation.Stage == "source-deleted")
        {
            await RequireDigestAsync(operation.Target, operation.SourceDigest, token);
            if (!operation.Copy) await identities.RelocateAsync(new(operation.Source), new(operation.Target), token);
            operation = operation with { Stage = "identity-relocated" }; await states.RecordOperationAsync(operation);
        }
        if (operation.Stage == "identity-relocated")
        {
            if (operation.RestoreBackup is not null)
            {
                await RequireDigestAsync(operation.RestoreBackup, operation.RestoreDigest, token);
                if (!await MatchesAsync(operation.RestorePath!, operation.RestoreDigest!, token))
                {
                    if (File.Exists(operation.RestorePath)) throw new IOException("撤销恢复位置已有外部文件，拒绝覆盖。");
                    await CopyVerifiedAsync(operation.RestoreBackup, operation.RestorePath!, operation.RestoreDigest!, token);
                }
            }
            operation = operation with { Stage = "finalized" }; await states.RecordOperationAsync(operation);
        }
        if (operation.Stage != "finalized") throw new IOException("无法识别文件操作阶段：" + operation.Stage);
        if (File.Exists(operation.Temporary)) File.Delete(operation.Temporary);
        await states.RemoveOperationAsync(operation.Id);
    }
    /// <summary>输入文件路径，流式生成 SHA256；不将摘要当作外部移动身份匹配。</summary>
    private static async Task<string> DigestAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
    }
    /// <summary>输入路径和预期摘要，返回是否匹配；不存在视为不匹配。</summary>
    private static async Task<bool> MatchesAsync(string? path, string? digest, CancellationToken token)
        => path is not null && digest is not null && File.Exists(path) && await DigestAsync(path, token) == digest;
    /// <summary>摘要缺失、文件缺失或外部修改均中止破坏性步骤。</summary>
    private static async Task RequireDigestAsync(string? path, string? digest, CancellationToken token)
    {
        if (!await MatchesAsync(path, digest, token)) throw new IOException("文件缺失或内容已变化，保留恢复记录：" + path);
    }
    /// <summary>输入源、目标及预期摘要，独占创建目标并刷盘后验证；失败不删除源。</summary>
    private static async Task CopyVerifiedAsync(string source, string target, string digest, CancellationToken token)
    {
        await RequireDigestAsync(source, digest, token);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true); }
        await RequireDigestAsync(target, digest, token);
    }
    /// <summary>撤销栈顶；被覆盖图片的恢复也写入同一操作日志，完成后才修改栈。</summary>
    public async Task<FileActionResult> UndoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
        => await Task.Run(() => ReplayAsync(false, conflict, token), CancellationToken.None);
    /// <summary>重做栈顶；摘要保证当前文件仍为历史中的内容。</summary>
    public async Task<FileActionResult> RedoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
        => await Task.Run(() => ReplayAsync(true, conflict, token), CancellationToken.None);
    /// <summary>输入重做标志，统一执行方向、附加备份恢复和成功历史转移。</summary>
    private async Task<FileActionResult> ReplayAsync(bool redo, ConflictChoice conflict, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await EnsureNoPendingAsync(token);
            var stack = redo ? _redo : _undo;
            if (stack.Count == 0) return new(false, "", null, "没有可撤销或重做的移动。");
            var record = stack[^1];
            var (result, operation) = await TransferAsync(redo ? record.Source : record.Target, redo ? record.Target : record.Source,
                false, conflict, record.Digest, record.Backup, record.BackupDigest, token);
            if (!result.Success) return result;
            stack.RemoveAt(stack.Count - 1);
            var next = redo ? _undo : _redo;
            next.Add(record with { Backup = operation!.Backup, BackupDigest = operation.BackupDigest }); Trim(next);
            return result;
        }
        catch (Exception error) { return new(false, "", null, ReaderException.From(error).Message); }
        finally { _gate.Release(); }
    }
    /// <summary>启动或用户重试时恢复日志；无法验证的记录保持原样并返回明确报告。</summary>
    public async Task<IReadOnlyList<FileRecoveryResult>> RecoverAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var results = new List<FileRecoveryResult>();
            foreach (var operation in await states.RecoveriesAsync(token))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (operation.Stage == "preparing" && operation.SourceDigest is not null)
                    {
                        // preparing 阶段尚未提交目标；只删除本应用命名的临时输出。
                        if (operation.Temporary is { } temporary && Path.GetFileName(temporary) == ".neeview-" + operation.Id + ".tmp" && File.Exists(temporary)) File.Delete(temporary);
                        await states.RemoveOperationAsync(operation.Id);
                    }
                    else await CompleteAsync(operation);
                    results.Add(new(operation.Id, true, "文件操作已恢复。"));
                }
                catch (Exception error) { results.Add(new(operation.Id, false, error.Message)); }
            }
            // 恢复可能完成之前失败的撤销；退出后本来就清空，因此不猜测旧栈方向。
            if (results.Count > 0) { _undo.Clear(); _redo.Clear(); }
            return results;
        }
        finally { _gate.Release(); }
    }
    /// <summary>正常退出等待当前文件操作越过提交点并完成日志写入。</summary>
    public async Task DrainAsync() { await _gate.WaitAsync(); _gate.Release(); }
}
