// Copyright (c) NeeLaboratory. 原ArchiveKey状态、缓存查找、重试及取消；窗口调用改为可等待替换点。
namespace NeeView;

/// <summary>原归档口令交互数据；只携带逻辑来源和重试状态，不携带口令。</summary>
public sealed record ArchiveKeyRequest(string ArchivePath, bool IsRetry);
/// <summary>后端确实遇到锁定文档时回报；损坏与其他错误不能被当作口令失败。</summary>
public sealed class ArchiveKeyRequiredException(string archivePath = "") : NotSupportedException("来源需要密码或密码不正确。")
{
    public string ArchivePath { get; } = archivePath;
}

/// <summary>保留原None/Completed/Canceled状态；每次打开拥有自己的重试对象。</summary>
public sealed class ArchiveKey(string fileName)
{
    public enum ArchiveKeyState { None, Completed, Canceled }
    public string Key { get; private set; } = "";
    public ArchiveKeyState State { get; private set; }
    public event EventHandler? KeyChanged;
    /// <summary>设置本次候选；只有实际打开成功才将其提交至进程缓存。</summary>
    public void SetKey(string key) { Key = key; State = ArchiveKeyState.Completed; KeyChanged?.Invoke(this, EventArgs.Empty); }
    public void SetState(ArchiveKeyState state) => State = state;
    /// <summary>初次尝试沿原路径命中缓存；失败后的同一请求不会重复读被拒绝候选。</summary>
    public void TryRestoreCache()
    { if (State == ArchiveKeyState.None && ArchiveKeyCache.Current.TryGetValue(fileName, out var key)) SetKey(key); }
    /// <summary>在后台槽之外等待界面；输入取消或迟到时不改变候选及缓存。</summary>
    /// <param name="request">独立表现端口令输入。</param><param name="token">切书/关闭取消。</param>
    /// <returns>是否获得非空候选，false保持旧书且结束本次打开。</returns>
    public async Task<bool> UpdateArchiveKeyByUserAsync(Func<ArchiveKeyRequest, CancellationToken, Task<string?>> request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (State == ArchiveKeyState.Canceled) return false;
        var value = await request(new(fileName, State == ArchiveKeyState.Completed), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(value)) { SetState(ArchiveKeyState.Canceled); return false; }
        SetKey(value); return true;
    }
}
