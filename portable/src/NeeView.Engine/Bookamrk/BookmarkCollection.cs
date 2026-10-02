// Copyright (c) NeeLaboratory. 移植自 NeeView/Bookamrk/BookmarkCollection.cs 及 BookmarkCollectionService.cs。
namespace NeeView;

/// <summary>原书签集合算法的 JSON 节点适配；保存事务由 SaveData 负责。</summary>
public sealed class BookmarkCollection(BookmarkNode items)
{
    public BookmarkNode Items { get; } = items;

    /// <summary>查询当前树中节点的父级；根节点及脱离树的节点返回 null。</summary>
    public BookmarkNode? ParentOf(BookmarkNode node) => Items.Walk().FirstOrDefault(e => e.Children?.Contains(node) == true);

    /// <summary>检查文件夹归属，拒绝过期节点和书籍节点。</summary>
    private void RequireFolder(BookmarkNode node)
    {
        if (!node.IsFolder || !Items.Walk().Contains(node)) throw new InvalidOperationException("书签文件夹已失效。");
    }

    /// <summary>按原名称校验替换路径分隔符；空书籍名用于恢复默认名称。</summary>
    public static string ValidateName(string? name) => string.IsNullOrWhiteSpace(name) ? "" : name.Trim().Replace('/', '_').Replace('\\', '_');

    /// <summary>在指定父级创建文件夹，按原规则为同名目录追加 (2)、(3)。</summary>
    /// <param name="parent">当前树中的根或文件夹。</param><param name="name">用户名称。</param>
    /// <returns>新节点，保留原 EntryTime 和 Children 格式。</returns>
    public BookmarkNode AddNewFolder(BookmarkNode parent, string? name)
    {
        RequireFolder(parent);
        var validName = ValidateName(name); if (validName.Length == 0) validName = "新建文件夹";
        var names = parent.Children!.Where(e => e.IsFolder).Select(e => e.Name).ToHashSet();
        var candidate = validName; var count = 1;
        while (names.Contains(candidate)) candidate = $"{validName} ({++count})";
        var node = new BookmarkNode { Name = candidate, EntryTime = DateTime.Now, Children = [] };
        parent.Children!.Add(node); return node;
    }

    /// <summary>添加书籍至选定文件夹；沿用服务的同父路径去重，不改变其他文件夹。</summary>
    /// <returns>新节点或当前父级已有的书签。</returns>
    public BookmarkNode AddTo(BookmarkNode parent, BookMemento memento)
    {
        RequireFolder(parent);
        var existing = parent.Children!.FirstOrDefault(e => !e.IsFolder && e.Path == memento.Path);
        if (existing is not null) return existing;
        var node = new BookmarkNode { Path = memento.Path, Page = memento.Page, Props = memento.ToPropertiesString() };
        parent.Children!.Add(node); return node;
    }

    /// <summary>移除当前树中非根节点；返回原父级与索引以供删除恢复。</summary>
    public BookmarkNodeMemento Remove(BookmarkNode node)
    {
        var parent = ParentOf(node) ?? throw new InvalidOperationException("书签节点已失效。");
        var memento = new BookmarkNodeMemento(parent, node, parent.Children!.IndexOf(node));
        parent.Children.Remove(node); return memento;
    }

    /// <summary>原父级仍存在时恢复同一节点，索引超长则追加；无效父级不会重建。</summary>
    /// <returns>是否实际恢复。</returns>
    public bool Restore(BookmarkNodeMemento memento)
    {
        if (!Items.Walk().Contains(memento.Parent) || Items.Walk().Contains(memento.Node)) return false;
        memento.Parent.Children!.Insert(Math.Clamp(memento.Index, 0, memento.Parent.Children.Count), memento.Node);
        return true;
    }

    /// <summary>按原最终索引移动节点；顺序移动与移入文件夹采用不同规则。</summary>
    /// <param name="index">移除源节点后的最终索引，越界钳制到首尾。</param>
    public BookmarkNode Move(BookmarkNode node, BookmarkNode parent, int index)
    {
        RequireFolder(parent);
        var oldParent = ParentOf(node) ?? throw new InvalidOperationException("书签节点已失效。");
        if (node.Walk().Contains(parent)) throw new InvalidOperationException("不能将文件夹移入自身或子文件夹。");
        if (ReferenceEquals(oldParent, parent))
        {
            index = Math.Clamp(index, 0, parent.Children!.Count - 1);
            var oldIndex = parent.Children.IndexOf(node);
            if (oldIndex != index) parent.Children.Move(oldIndex, index);
        }
        else
        {
            oldParent.Children!.Remove(node);
            parent.Children!.Insert(Math.Clamp(index, 0, parent.Children.Count), node);
        }
        return node;
    }

    /// <summary>原 MoveToChild：移入首位，同名文件夹合并，名称及路径相同的书籍去重。</summary>
    /// <returns>实际保留节点，用于界面继续选中。</returns>
    public BookmarkNode? MoveToChild(BookmarkNode node, BookmarkNode target)
    {
        if (!target.IsFolder) return null;
        RequireFolder(target);
        var parent = ParentOf(node) ?? throw new InvalidOperationException("书签节点已失效。");
        if (node.Walk().Contains(target) || ReferenceEquals(parent, target)) return null;
        var conflict = target.Children!.FirstOrDefault(e => IsEqual(node, e));
        if (conflict is not null)
        {
            if (node.IsFolder) Merge(node, conflict); else Remove(node);
            return conflict;
        }
        return Move(node, target, 0);
    }

    /// <summary>沿用 BookmarkFolder.IsEqual 及 Bookmark.IsEqual，书籍比较显示名称与路径。</summary>
    private static bool IsEqual(BookmarkNode left, BookmarkNode right) => left.IsFolder == right.IsFolder &&
        (left.IsFolder ? left.Name == right.Name : left.DisplayName == right.DisplayName && left.Path == right.Path);

    /// <summary>按原 Merge 递归合并；CloneChildren 是列表快照，子节点身份和未知字段保持。</summary>
    private void Merge(BookmarkNode node, BookmarkNode target)
    {
        if (!node.IsFolder || !target.IsFolder || node.Walk().Contains(target)) throw new InvalidOperationException("无法合并书签文件夹。");
        Remove(node);
        MergeDetached(node, target);
    }

    /// <summary>合并已从源移除的子文件夹，继续使用原子节点引用。</summary>
    private void MergeDetached(BookmarkNode node, BookmarkNode target)
    {
        foreach (var child in node.Children!.ToArray())
        {
            // 原算法先从源移除，再递归合并或追加，不复制业务节点。
            node.Children!.Remove(child);
            var conflict = target.Children!.FirstOrDefault(e => IsEqual(child, e));
            if (conflict is not null) { if (child.IsFolder) MergeDetached(child, conflict); }
            else target.Children!.Add(child);
        }
    }

    /// <summary>普通书签更名；同父同名文件夹必须提供界面确认的确切目标，取消不变更。</summary>
    public BookmarkNode Rename(BookmarkNode node, string name, BookmarkNode? confirmedTarget)
    {
        var parent = ParentOf(node) ?? throw new InvalidOperationException("书签节点已失效。");
        var validName = ValidateName(name);
        if (node.IsFolder)
        {
            if (validName.Length == 0) return node;
            var conflict = parent.Children!.FirstOrDefault(e => e != node && e.IsFolder && e.Name == validName);
            // 确认对话框返回时若原目标已消失，禁止把过期“合并”变成普通更名。
            if (confirmedTarget is not null && conflict is null) throw new InvalidOperationException("合并目标已变化，请重新操作。");
            if (conflict is not null)
            {
                if (!ReferenceEquals(conflict, confirmedTarget)) throw new BookmarkMergeRequiredException(conflict);
                Merge(node, conflict); return conflict;
            }
        }
        node.Name = validName.Length == 0 || (!node.IsFolder && validName == System.IO.Path.GetFileName(node.Path)) ? null : validName;
        return node;
    }
}

/// <summary>原 TreeListNodeMemento 的节点/父级/顺序恢复记录，仅进程内保留。</summary>
public sealed record BookmarkNodeMemento(BookmarkNode Parent, BookmarkNode Node, int Index);

/// <summary>表现层必须确认的同名文件夹目标；Engine 不创建对话框。</summary>
public sealed class BookmarkMergeRequiredException(BookmarkNode target) : InvalidOperationException("存在同名书签文件夹，需要确认合并。")
{
    public BookmarkNode Target { get; } = target;
}
