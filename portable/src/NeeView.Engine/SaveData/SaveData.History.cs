namespace NeeView;

public sealed partial class SaveData
{
    /// <summary>原无效历史检查：完整快照全部检查成功后一次提交；失败/取消保留所有记录。</summary>
    /// <param name="exists">来源可靠存在检查，不能用File.Exists吞掉权限错误。</param>
    /// <param name="token">重复请求或窗口关闭取消。</param><returns>实际删除数。</returns>
    public async Task<int> RemoveUnlinkedHistoryAsync(Func<string, CancellationToken, Task<bool>> exists, CancellationToken token = default)
    {
        Dictionary<string, string> snapshot;
        await _gate.WaitAsync(token);
        try { snapshot = (_history["Items"] as System.Text.Json.Nodes.JsonArray)?.OfType<System.Text.Json.Nodes.JsonObject>()
            .Where(e => e["Path"] is not null).GroupBy(e => e["Path"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(e => e.Key, e => e.First().ToJsonString(), StringComparer.Ordinal) ?? []; }
        finally { _gate.Release(); }
        var missing = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in snapshot)
        { token.ThrowIfCancellationRequested(); if (!await exists(item.Key, token)) missing[item.Key] = item.Value; }
        token.ThrowIfCancellationRequested();
        // 探测期间新访问/阅读更新的同路径项不属于该旧快照，留给下一次清理。
        return await EditHistoryAsync(missing.Keys.ToHashSet(StringComparer.Ordinal), token, missing);
    }
}
