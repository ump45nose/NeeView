using NeeView.Core;
namespace NeeView.Desktop;

/// <summary>纯表现层树节点，避免数据库节点依赖 TreeView。</summary>
public sealed class BookmarkNode(Bookmark item)
{
    public Bookmark Item { get; } = item;
    public List<BookmarkNode> Children { get; } = [];
    /// <summary>输入平面持久化节点，按父子关系和顺序构树；孤儿节点保留，循环节点回到根。</summary>
    public static IReadOnlyList<BookmarkNode> Build(IReadOnlyList<Bookmark> bookmarks)
    {
        var nodes = bookmarks.ToDictionary(b => b.Id, b => new BookmarkNode(b)); var roots = new List<BookmarkNode>();
        foreach (var node in nodes.Values.OrderBy(n => n.Item.Order))
        {
            var parent = node.Item.ParentId; var seen = new HashSet<string> { node.Item.Id }; var cursor = parent; var cycle = false;
            while (cursor is not null && nodes.TryGetValue(cursor, out var ancestor)) { if (!seen.Add(cursor)) { cycle = true; break; } cursor = ancestor.Item.ParentId; }
            if (!cycle && parent is not null && nodes.TryGetValue(parent, out var owner)) owner.Children.Add(node); else roots.Add(node);
        }
        return roots;
    }
}
