using NeeView;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    /// <summary>本次打开沿原逻辑来源保存候选；嵌套代理随机路径不能成为缓存键。</summary>
    private sealed class ArchiveKeys
    {
        private readonly Dictionary<string, ArchiveKey> _keys = new(StringComparer.Ordinal);
        internal ArchiveKey Get(string path)
        {
            if (!_keys.TryGetValue(path, out var key)) { key = new(path); key.TryRestoreCache(); _keys.Add(path, key); }
            return key;
        }
        /// <summary>所有来源实际准备成功后才缓存；错误输入/取消不会污染下一次打开。</summary>
        internal void Commit() { foreach (var pair in _keys) if (pair.Value.State == ArchiveKey.ArchiveKeyState.Completed) ArchiveKeyCache.Current.Add(pair.Key, pair.Value.Key); }
    }
    /// <summary>重试在SourceIo槽之外；每个尝试完整释放失败父链，晚到结果按既有代次拒绝。</summary>
    private static async Task<Archive> OpenWithKeysAsync(Func<ArchiveKeys, Task<Archive>> open,
        Func<ArchiveKeyRequest, CancellationToken, Task<string?>>? requestKey, CancellationToken token)
    {
        var keys = new ArchiveKeys();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result = await open(keys).ConfigureAwait(false);
                try { token.ThrowIfCancellationRequested(); keys.Commit(); return result; }
                catch { await result.DisposeAsync(); throw; }
            }
            catch (ArchiveKeyRequiredException error)
            {
                if (requestKey is null) throw;
                var key = keys.Get(error.ArchivePath);
                if (!await key.UpdateArchiveKeyByUserAsync(requestKey, token).ConfigureAwait(false))
                    throw new OperationCanceledException("已取消密码输入。", token);
            }
        }
    }
    /// <summary>与无交互入口同一来源链，只有明确阅读调用传入口令输入。</summary>
    public Task<Archive> OpenAsync(string path, CancellationToken token, Func<ArchiveKeyRequest, CancellationToken, Task<string?>> requestKey)
        => OpenWithKeysAsync(keys => OpenPathAsync(path, keys, token), requestKey, token);
}
