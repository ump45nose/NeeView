using NeeView;
namespace NeeView.Backends;

/// <summary>原 GetFileProxy/TempFile 的 Mac 替换点；请求级提取、进程级剪贴板租约。</summary>
public sealed class ArchiveEntryRealizer(string? temporaryRoot = null) : IArchiveEntryRealizer, IAsyncDisposable
{
    private readonly string _root = temporaryRoot ?? Path.Combine(ArchiveFactory.TemporaryDirectory, "Realized");
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<RealizedFilePathList> _retired = [];
    // 请求租约可能在失败后离开作用域；后端仍持有清理失败目录，退出可重试。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _pendingCleanup = new(StringComparer.Ordinal);
    private RealizedFilePathList? _clipboard;
    private bool _disposed;
    /// <summary>提取到随机独立目录并保持原叶文件名；不沿归档内部路径创建目录。</summary>
    /// <param name="entry">来源仍由书籍拥有。</param><param name="remainingBytes">本批剩余磁盘预算。</param>
    /// <param name="token">读取与写入准备可取消。</param><returns>与来源生命周期分离、调用方所有的租约。</returns>
    public async Task<RealizedFileLease> ExtractAsync(ArchiveEntry entry, long remainingBytes, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_retired.Count > 0 || !_pendingCleanup.IsEmpty) throw new IOException("上一批临时实体尚未清理，暂停新的解压复制。");
            if (!entry.CanRealize() || entry.FilePath is not null) throw new NotSupportedException("只提取归档中的文件条目。");
            if (remainingBytes < 0 || entry.Length > remainingBytes) throw new NotSupportedException("归档实体化批次超过 2 GiB 临时预算。");
            return await Task.Run(async () =>
            {
                var name = Path.GetFileName(entry.EntryName);
                if (name is "" or "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("归档条目没有有效文件名。");
                var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                string path = Path.Combine(directory, name);
                // 对应用临时根逐级检查链接；不递归清理任意用户目录。
                for (var parent = new DirectoryInfo(_root); parent is not null; parent = parent.Parent)
                    if (parent.LinkTarget is not null && !(parent.FullName == "/var" && parent.ResolveLinkTarget(true)?.FullName == "/private/var"))
                        throw new IOException("临时实体根包含符号链接。");
                Directory.CreateDirectory(directory);
                try
                {
                    long length = 0;
                    await using (var input = await entry.Archive.OpenEntryAsync(entry, token).ConfigureAwait(false))
                    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true))
                    {
                        byte[] buffer = new byte[81920]; int read;
                        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                        {
                            token.ThrowIfCancellationRequested(); length += read;
                            if (length > remainingBytes) throw new InvalidDataException("归档实体化批次超过临时预算。");
                            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        }
                        if (entry.Length >= 0 && length != entry.Length) throw new InvalidDataException("归档实体化文件长度与索引不符。");
                        await output.FlushAsync(token).ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();
                    return new RealizedFileLease(path, length, () => DeleteOwnedAsync(directory));
                }
                catch { await DeleteOwnedAsync(directory); throw; }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>成功写入后接管租约；清理旧材料失败不把已提交剪贴板伪装成失败。</summary>
    /// <param name="files">调用方在成功转交后不再释放。</param><returns>所有权转交完成任务。</returns>
    public async Task RetainClipboardAsync(RealizedFilePathList files)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clipboard is { } previous) _retired.Add(previous);
            _clipboard = files;
            foreach (var old in _retired.ToArray())
            {
                try { await old.DisposeAsync(); _retired.Remove(old); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
            // 无法清理的旧材料不是无限缓存；拒绝后续提取，退出时仍重试。
        }
        finally { _gate.Release(); }
    }
    /// <summary>正常进程退出清理剪贴板临时材料，关窗驻留不调用。</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_clipboard is { } files) { _retired.Add(files); _clipboard = null; }
            List<Exception> errors = [];
            foreach (var old in _retired.ToArray())
            { try { await old.DisposeAsync(); _retired.Remove(old); } catch (Exception ex) { errors.Add(ex); } }
            foreach (var directory in _pendingCleanup.Keys)
            { try { await DeleteOwnedAsync(directory); } catch (Exception ex) { errors.Add(ex); } }
            if (errors.Count > 0) throw new AggregateException("进程临时实体清理失败，可重试退出。", errors);
            _disposed = true;
        }
        finally { _gate.Release(); }
    }
    /// <summary>释放失败时将后端生成目录登记到进程重试队列；不改变剪贴板所有权。</summary>
    /// <param name="directory">本次提取拥有的独立目录。</param><returns>清理任务；失败仍向调用方回报。</returns>
    private async ValueTask DeleteOwnedAsync(string directory)
    {
        try { await DeleteAsync(directory); _pendingCleanup.TryRemove(directory, out _); }
        catch { _pendingCleanup.TryAdd(directory, 0); throw; }
    }
    /// <summary>只移除本次随机目录；被外部替换成链接时拒绝递归删除。</summary>
    /// <param name="directory">此后端生成并拥有的目录。</param><returns>本地后台清理任务。</returns>
    private static ValueTask DeleteAsync(string directory) => new(Task.Run(() =>
    {
        var info = new DirectoryInfo(directory);
        if (info.LinkTarget is not null) throw new IOException("临时实体目录已被替换成链接，未删除。");
        if (info.Exists) info.Delete(true);
    }));
}
