using System.Security.Cryptography;
using NeeView.Core;

namespace NeeView.Application;

/// <summary>进程共享文件操作协调器；成功文件结果才改变撤销历史。</summary>
public sealed class FileActionService(IIdentityRegistry identities, IReaderStateStore states, IPlatformService platform, string backupRoot) : IFileActionService
{
    private sealed record MoveRecord(string Source, string Target, string? Backup);
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
    /// <summary>裁剪最旧记录；备份保留到缓存清理，避免删除其它恢复记录依赖。</summary>
    private void Trim(List<MoveRecord> history) { while (history.Count > _capacity) history.RemoveAt(0); }
    /// <summary>输入普通目录图片和目标，返回实际路径；归档内部条目不可修改。</summary>
    public async Task<FileActionResult> ExecuteAsync(PageDescriptor page, FileActionKind action, string? target,
        ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
    {
        if (page.Locator.Entry is not null) return new(false, page.Locator.Path, null, "压缩包内部条目只读。");
        await _gate.WaitAsync(token);
        try
        {
            if (action == FileActionKind.Trash)
            {
                await platform.TrashAsync(page.Locator.Path, token);
                return new(true, page.Locator.Path, null);
            }
            if (target is null) return new(false, page.Locator.Path, null, "未指定目标。");
            var destination = action == FileActionKind.Rename ? Path.GetFullPath(target) : Path.Combine(Path.GetFullPath(target), Path.GetFileName(page.Locator.Path));
            var (result, backup) = await TransferAsync(page.Locator.Path, destination, action == FileActionKind.Copy, conflict, token);
            if (result.Success && action != FileActionKind.Copy)
            {
                _redo.Clear();
                if (_capacity > 0) { _undo.Add(new(page.Locator.Path, destination, backup)); Trim(_undo); }
            }
            return result;
        }
        catch (Exception error) { return new(false, page.Locator.Path, target, ReaderException.From(error).Message); }
        finally { _gate.Release(); }
    }
    /// <summary>写临时目标、验证 SHA256，再删除源；覆盖前备份且全程留恢复记录。</summary>
    private async Task<(FileActionResult Result, string? Backup)> TransferAsync(string source, string target, bool copy, ConflictChoice conflict, CancellationToken token)
    {
        source = Path.GetFullPath(source); target = Path.GetFullPath(target);
        if (source == target) return (new(false, source, target, "源和目标相同。"), null);
        if (!File.Exists(source)) return (new(false, source, target, "源文件不存在。"), null);
        if (Directory.Exists(target)) return (new(false, source, target, "目标是目录。"), null);
        if (!Directory.Exists(Path.GetDirectoryName(target))) return (new(false, source, target, "目标目录不存在。"), null);
        if (File.Exists(target) && conflict != ConflictChoice.Overwrite) return (new(false, source, target, "目标同名文件已存在，请选择覆盖或取消。"), null);
        var id = Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".neeview-" + id + ".tmp");
        string? backup = null; var committed = false; var sourceDeleted = false;
        var operation = new RecoveryOperation(id, source, target, temporary, null, "preparing");
        await states.RecordOperationAsync(operation, token);
        try
        {
            if (File.Exists(target))
            {
                Directory.CreateDirectory(backupRoot); backup = Path.Combine(backupRoot, id + ".backup");
                await CopyVerifiedAsync(target, backup, token);
                operation = operation with { Backup = backup }; await states.RecordOperationAsync(operation, token);
            }
            await CopyVerifiedAsync(source, temporary, token);
            token.ThrowIfCancellationRequested();
            // 从提交点起不再接受取消，保证目标落盘与源删除形成完整结果。
            File.Move(temporary, target, conflict == ConflictChoice.Overwrite); committed = true;
            await states.RecordOperationAsync(operation with { Stage = "target-committed" });
            if (!copy)
            {
                File.Delete(source); sourceDeleted = true;
                await states.RecordOperationAsync(operation with { Stage = "source-deleted" });
                await identities.RelocateAsync(new(source), new(target), CancellationToken.None);
            }
            await states.RemoveOperationAsync(id);
            var content = await identities.GetContentAsync(new(target), CancellationToken.None);
            return (new(true, source, target, Content: content), backup);
        }
        catch
        {
            try
            {
                if (sourceDeleted) await CopyVerifiedAsync(target, source, CancellationToken.None);
                if (committed)
                {
                    if (backup is not null) await CopyVerifiedAsync(backup, target, CancellationToken.None, true);
                    else File.Delete(target);
                }
                if (File.Exists(temporary)) File.Delete(temporary);
                await states.RemoveOperationAsync(id);
            }
            catch { /* 无法回滚时保留数据库恢复记录与备份，供下次启动处理。 */ }
            throw;
        }
    }
    /// <summary>流式复制并验证内容，源变化时拒绝删除源。</summary>
    private static async Task CopyVerifiedAsync(string source, string target, CancellationToken token, bool overwrite = false)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
        await using (var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true); }
        await using var left = File.OpenRead(source); await using var right = File.OpenRead(target);
        var a = await SHA256.HashDataAsync(left, token); var b = await SHA256.HashDataAsync(right, token);
        if (!a.AsSpan().SequenceEqual(b)) throw new IOException("复制完整性校验失败，源文件已保留。");
    }
    /// <summary>撤销栈顶；失败或取消保持历史不变，并恢复被覆盖的原目标。</summary>
    public async Task<FileActionResult> UndoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_undo.Count == 0) return new(false, "", null, "没有可撤销的移动。");
            var record = _undo[^1];
            var (result, displaced) = await TransferAsync(record.Target, record.Source, false, conflict, token);
            if (!result.Success) return result;
            if (record.Backup is { } backup) await CopyVerifiedAsync(backup, record.Target, CancellationToken.None);
            _undo.RemoveAt(_undo.Count - 1); _redo.Add(record with { Backup = displaced }); Trim(_redo);
            return result;
        }
        catch (Exception error) { return new(false, "", null, ReaderException.From(error).Message); }
        finally { _gate.Release(); }
    }
    /// <summary>重做栈顶；目标覆盖规则与普通移动相同。</summary>
    public async Task<FileActionResult> RedoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_redo.Count == 0) return new(false, "", null, "没有可重做的移动。");
            var record = _redo[^1];
            var (result, backup) = await TransferAsync(record.Source, record.Target, false, conflict, token);
            if (!result.Success) return result;
            if (record.Backup is { } displaced) await CopyVerifiedAsync(displaced, record.Source, CancellationToken.None);
            _redo.RemoveAt(_redo.Count - 1); _undo.Add(record with { Backup = backup }); Trim(_undo);
            return result;
        }
        catch (Exception error) { return new(false, "", null, ReaderException.From(error).Message); }
        finally { _gate.Release(); }
    }
}
