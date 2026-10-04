using NeeView;
namespace NeeView.Backends;

/// <summary>原Temporary.TempDownloadDirectory替换点；只拥有本进程生成的.nvpls。</summary>
public sealed class TemporaryPlaylistService(string? temporaryRoot = null) : ITemporaryPlaylistService, IAsyncDisposable
{
    private readonly string _directory = System.IO.Path.Combine(temporaryRoot ?? ArchiveFactory.TemporaryDirectory, "Playlists", Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim _gate = new(1);
    private long _bytes;
    private bool _disposed;
    private bool _cleanupFailed;
    /// <summary>创建原格式临时列表；32MiB进程预算，关窗/切书不删历史导航所需文件。</summary>
    /// <param name="paths">原接收顺序，保留重复及混合目录。</param><param name="token">准备取消。</param>
    /// <returns>随机独立列表地址，正常退出统一清理。</returns>
    public async Task<string> CreateAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        var bytes = PlaylistSourceTools.CreateTemporarySource(paths);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cleanupFailed) throw new IOException("临时列表尚未清理，暂停新的多文件打开。");
            if (_bytes + bytes.LongLength > 32L * 1024 * 1024) throw new NotSupportedException("临时列表已达到32MiB进程预算，请退出后重试。");
            return await Task.Run(async () =>
            {
                for (var parent = new DirectoryInfo(_directory); parent is not null; parent = parent.Parent)
                    if (parent.LinkTarget is not null && !(parent.FullName == "/var" && parent.ResolveLinkTarget(true)?.FullName == "/private/var"))
                        throw new IOException("临时列表根包含符号链接。");
                Directory.CreateDirectory(_directory);
                var path = System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + PlaylistSourceTools.Extension);
                try
                {
                    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true))
                    { await output.WriteAsync(bytes, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); }
                    token.ThrowIfCancellationRequested(); _bytes += bytes.LongLength; return path;
                }
                catch
                {
                    try { File.Delete(path); } catch { _cleanupFailed = true; throw; }
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    /// <summary>正常进程退出仅清理此进程独立目录，链接替换拒绝删除并允许重试。</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var directory = new DirectoryInfo(_directory);
                if (directory.LinkTarget is not null) throw new IOException("临时列表目录被替换成链接，未删除。");
                if (directory.Exists) directory.Delete(true);
            }).ConfigureAwait(false);
            _bytes = 0; _disposed = true;
        }
        catch { _cleanupFailed = true; throw; }
        finally { _gate.Release(); }
    }
}
