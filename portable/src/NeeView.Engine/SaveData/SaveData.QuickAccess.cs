using System.Text.Json.Nodes;
namespace NeeView;
public sealed partial class SaveData
{
    private JsonObject _quickAccess = new();
    public QuickAccessCollection QuickAccess { get; } = new();
    public event EventHandler? QuickAccessChanged;
    /// <summary>原快速访问编辑进入唯一保存锁/JSON事务；失败原地回滚节点身份及顺序。</summary>
    /// <param name="edit">只操作当前QuickAccess权威集合。</param><param name="token">等待和准备阶段取消。</param>
    /// <returns>仅在全部文件提交成功后返回编辑结果。</returns>
    public async Task<T> EditQuickAccessAsync<T>(Func<QuickAccessCollection, T> edit, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        var snapshots = QuickAccess.Root.Walk().Select(node => (Node: node, node.Name, node.Path, Children: node.Children?.ToArray())).ToArray();
        try { var result = edit(QuickAccess); await WritePairAsync(token); return result; }
        catch
        {
            foreach (var item in snapshots)
            { item.Node.Name = item.Name; item.Node.Path = item.Path; if (item.Children is null) continue; item.Node.Children!.Clear(); foreach (var child in item.Children) item.Node.Children.Add(child); }
            throw;
        }
        finally { _gate.Release(); QuickAccessChanged?.Invoke(this, EventArgs.Empty); }
    }
}
