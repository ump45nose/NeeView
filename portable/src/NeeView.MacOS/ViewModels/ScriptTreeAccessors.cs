// Copyright (c) NeeLaboratory. 原Bookshelf/BookmarkFolderTree与Node/Source访问器关系，MIT。
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace NeeView.MacOS.ViewModels;
/// <summary>原树API包装唯一目录树和JSON节点；包装器不保存额外选择或展开状态。</summary>
public sealed class BookshelfFolderTreeAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, ScriptPanelBindings? bindings)
{
    private FolderTreeModel Tree => model.Operation.Bookshelf.FolderTree;
    public DirectoryNodeAccessor DirectoryNode => context.Read(() => new DirectoryNodeAccessor(context, Tree.Root));
    public QuickAccessNodeAccessor QuickAccessNode => new(context, model, context.State.QuickAccess.Root);
    public BookmarkFolderNodeAccessor BookmarkNode => new(context, context.State.BookmarkRoot, bindings, "BookshelfBookmarkTree");
    public NodeAccessor? SelectedItem
    {
        get => context.Read<NodeAccessor?>(() => Tree.SelectedItem switch
        { DirectoryNode node => new DirectoryNodeAccessor(context, node), QuickAccessDirectoryNode node => new QuickAccessNodeAccessor(context, model, node.Value), _ => bindings?.Tree?.Invoke("BookshelfBookmarkTree")?.SelectedItem is BookmarkNode b ? BookmarkNode.Create(b) : null });
        set => context.Write(() =>
        {
            if (value is DirectoryNodeAccessor directory) Tree.SelectedItem = directory.Source;
            else if (value is QuickAccessNodeAccessor quick)
                Tree.SelectedItem = Tree.QuickAccessRoot is { } root ? Walk(root).OfType<QuickAccessDirectoryNode>().FirstOrDefault(n => ReferenceEquals(n.Value, quick.Source)) : null;
            else if (value is BookmarkFolderNodeAccessor bookmark) { if (bindings?.Tree?.Invoke("BookshelfBookmarkTree") is { } view) view.SelectedItem = bookmark.Source; }
            else Tree.SelectedItem = null;
        });
    }
    internal static IEnumerable<FolderTreeNodeBase> Walk(FolderTreeNodeBase node)
    { yield return node; foreach (var child in node.ChildrenRaw ?? []) foreach (var nested in Walk(child)) yield return nested; }
    public void Expand(string path) => context.Run(async () => { if (await Tree.SyncDirectoryAsync(path, true, context.Token) is null) throw new IOException(Tree.Error ?? "树路径不可访问。"); });
}
public sealed class BookmarkFolderTreeAccessor(ScriptAccessContext context, ScriptPanelBindings? bindings, string key)
{
    public BookmarkFolderNodeAccessor BookmarkNode => new(context, context.State.BookmarkRoot, bindings, key);
    public NodeAccessor? SelectedItem
    {
        get => context.Read(() => bindings?.Tree?.Invoke(key)?.SelectedItem is BookmarkNode node ? BookmarkNode.Create(node) : null);
        set => context.Write(() =>
        {
            var view = bindings?.Tree?.Invoke(key) ?? throw new NotSupportedException("书签树未装配。");
            if (value is not null && (value is not BookmarkFolderNodeAccessor b || !context.State.BookmarkRoot.Walk().Contains(b.Source))) throw new ArgumentException("节点不属于当前书签树。");
            view.SelectedItem = (value as BookmarkFolderNodeAccessor)?.Source;
        });
    }
}
public abstract class NodeAccessor
{
    public abstract bool IsDisposed { get; }
    public abstract bool IsExpanded { get; set; }
    public abstract int Index { get; }
    public abstract string Name { get; }
    public abstract NodeAccessor? Parent { get; }
    public abstract NodeAccessor[]? Children { get; }
    public abstract object Value { get; }
    public string ValueType => Value.GetType().Name;
    public virtual NodeAccessor Add() => Add(null);
    public abstract NodeAccessor Add(IDictionary<string, object?>? parameter);
    public virtual NodeAccessor Insert(int index) => Insert(index, null);
    public abstract NodeAccessor Insert(int index, IDictionary<string, object?>? parameter);
    public abstract bool Remove();
    public abstract void MoveTo(int newIndex);
}
/// <summary>普通目录只读延迟节点；增删重排按原API明确不支持，不修改实体文件。</summary>
public sealed class DirectoryNodeAccessor(ScriptAccessContext context, DirectoryNode node) : NodeAccessor
{
    internal DirectoryNode Source => node;
    public override bool IsDisposed => context.Read(() => node.IsDisposed);
    public override bool IsExpanded { get => context.Read(() => node.IsExpanded); set => context.Write(() => node.IsExpanded = value); }
    public override int Index => context.Read(() => node.Parent?.Children.IndexOf(node) ?? -1);
    public override string Name => context.Read(() => node.DisplayName);
    public override NodeAccessor? Parent => context.Read(() => node.Parent is DirectoryNode parent ? new DirectoryNodeAccessor(context, parent) : null);
    public override NodeAccessor[] Children
    {
        get
        {
            if (context.Read(() => node.ChildrenRaw is null || node.ChildrenRaw.Any(n => n.IsPlaceholder)))
                context.Run(async () => { if (!await node.CreateChildrenAsync(token: context.Token)) throw new IOException(node.Error ?? "目录暂不可访问。"); });
            return context.Read(() => node.ChildrenRaw?.OfType<DirectoryNode>().Select(n => (NodeAccessor)new DirectoryNodeAccessor(context, n)).ToArray() ?? []);
        }
    }
    public override DirectoryNodeSource Value => new(context, node);
    public override NodeAccessor Add(IDictionary<string, object?>? parameter) => throw new NotSupportedException("普通目录树节点不支持Add。");
    public override NodeAccessor Insert(int index, IDictionary<string, object?>? parameter) => throw new NotSupportedException("普通目录树节点不支持Insert。");
    public override bool Remove() => throw new NotSupportedException("普通目录树节点不支持Remove。");
    public override void MoveTo(int newIndex) => throw new NotSupportedException("普通目录树节点不支持MoveTo。");
}
public sealed class DirectoryNodeSource(ScriptAccessContext context, DirectoryNode node)
{
    public string Path => context.Read(() => node.Path);
    public string Name => context.Read(() => node.DisplayName);
}
/// <summary>快速访问增删、改名和顺序经已有五JSON事务保存，失败原地回滚。</summary>
public sealed class QuickAccessNodeAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, QuickAccessTreeNode node) : NodeAccessor
{
    internal QuickAccessTreeNode Source => node;
    private QuickAccessCollection Collection => context.State.QuickAccess;
    private QuickAccessDirectoryNode? Presented => model.Operation.Bookshelf.FolderTree.QuickAccessRoot is { } root
        ? BookshelfFolderTreeAccessor.Walk(root).OfType<QuickAccessDirectoryNode>().FirstOrDefault(n => ReferenceEquals(n.Value, node)) : null;
    private void Require() { if (IsDisposed) throw new ObjectDisposedException(nameof(QuickAccessNodeAccessor)); }
    public override bool IsDisposed => context.Read(() => !Collection.Root.Walk().Contains(node));
    public override bool IsExpanded { get => context.Read(() => node.IsFolder && Presented?.IsExpanded == true); set => context.Write(() => { Require(); if (node.IsFolder && Presented is { } actual) actual.IsExpanded = value; }); }
    public override int Index => context.Read(() => Collection.ParentOf(node)?.Children?.IndexOf(node) ?? -1);
    public override string Name => context.Read(() => node.DisplayName);
    public override NodeAccessor? Parent => context.Read(() => Collection.ParentOf(node) is { } parent ? new QuickAccessNodeAccessor(context, model, parent) : null);
    public override NodeAccessor[]? Children => context.Read(() => node.Children?.Select(n => (NodeAccessor)new QuickAccessNodeAccessor(context, model, n)).ToArray());
    public override QuickAccessNodeSource Value => new(context, node);
    public override NodeAccessor Add(IDictionary<string, object?>? parameter) => Insert(-1, parameter);
    public override NodeAccessor Insert(int index, IDictionary<string, object?>? parameter)
    {
        Require(); QuickAccessTreeNode? result = null;
        context.Run(async () => result = await context.State.EditQuickAccessAsync(c =>
        {
            var name = ScriptTreeParameters.String(parameter, "Name");
            var folder = ScriptTreeParameters.String(parameter, "Type") == "folder";
            var created = folder ? c.AddFolder(node, name) : c.Add(node, ScriptTreeParameters.String(parameter, "Path") ?? model.Operation.Bookshelf.Place ?? throw new InvalidOperationException("书架还没有目录。"), name);
            if (index >= 0) node.Children!.Move(node.Children.IndexOf(created), Math.Clamp(index, 0, node.Children.Count - 1));
            return created;
        }, context.Token));
        return new QuickAccessNodeAccessor(context, model, result!);
    }
    public override bool Remove() { Require(); context.Run(() => context.State.EditQuickAccessAsync(c => { c.Remove(node); return true; }, context.Token)); return true; }
    public override void MoveTo(int newIndex)
    {
        Require(); context.Run(() => context.State.EditQuickAccessAsync(c =>
        {
            var parent = c.ParentOf(node) ?? throw new NotSupportedException("根节点不能移动。");
            var count = parent.Children!.Count; parent.Children.Move(parent.Children.IndexOf(node), Math.Clamp(newIndex, 0, count - 1)); return true;
        }, context.Token));
    }
}
public sealed class QuickAccessNodeSource(ScriptAccessContext context, QuickAccessTreeNode node)
{
    public string Name { get => context.Read(() => node.DisplayName); set => context.Run(() => context.State.EditQuickAccessAsync(c => { c.Rename(node, value); return true; }, context.Token)); }
    public string? Path { get => context.Read(() => node.Path); set => context.Run(() => context.State.EditQuickAccessAsync(c =>
        { if (node.IsFolder || !c.Root.Walk().Contains(node)) throw new NotSupportedException("请选择有效快速访问项。"); if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("路径不能为空。"); node.Path = value; return true; }, context.Token)); }
}
/// <summary>书签包装原权威节点，选择和展开直接读写当前真实TreeView。</summary>
public sealed class BookmarkFolderNodeAccessor(ScriptAccessContext context, BookmarkNode node, ScriptPanelBindings? bindings, string key) : NodeAccessor
{
    internal BookmarkNode Source => node;
    internal BookmarkFolderNodeAccessor Create(BookmarkNode value) => new(context, value, bindings, key);
    private TreeViewItem? Container => bindings?.Tree?.Invoke(key)?.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(c => ReferenceEquals(c.DataContext, node));
    private void Require() { if (IsDisposed) throw new ObjectDisposedException(nameof(BookmarkFolderNodeAccessor)); }
    public override bool IsDisposed => context.Read(() => !context.State.BookmarkRoot.Walk().Contains(node));
    public override bool IsExpanded
    {
        get => context.Read(() => ReferenceEquals(node, context.State.BookmarkRoot) || Container?.IsExpanded == true);
        set => context.Write(() => { Require(); if (ReferenceEquals(node, context.State.BookmarkRoot)) return; (Container ?? throw new InvalidOperationException("请先展开父节点使该书签节点可见。")).IsExpanded = value; });
    }
    public override int Index => context.Read(() => context.State.Bookmarks.ParentOf(node)?.Children?.IndexOf(node) ?? -1);
    public override string Name => context.Read(() => node.DisplayName);
    public override NodeAccessor? Parent => context.Read(() => context.State.Bookmarks.ParentOf(node) is { } parent ? Create(parent) : null);
    public override NodeAccessor[]? Children => context.Read(() => node.Children?.Where(n => n.IsFolder).Select(n => (NodeAccessor)Create(n)).ToArray());
    public override BookmarkFolderNodeSource Value => new(context, node);
    public override NodeAccessor Add(IDictionary<string, object?>? parameter)
    {
        Require(); BookmarkNode? result = null;
        context.Run(async () => result = await context.State.AddBookmarkFolderAsync(node, ScriptTreeParameters.String(parameter, "Name") ?? "新建文件夹", context.Token));
        return Create(result!);
    }
    // 固定原版BookmarkFolderNodeAccessor.Insert明确走Add，保留其追加语义。
    public override NodeAccessor Insert(int index, IDictionary<string, object?>? parameter) => Add(parameter);
    public override bool Remove() { Require(); context.Run(() => context.State.RemoveBookmarkAsync(node, context.Token)); return true; }
    public override void MoveTo(int newIndex)
    { Require(); context.Run(() => context.State.MoveBookmarkAsync(node, context.State.Bookmarks.ParentOf(node) ?? throw new NotSupportedException("根节点不能移动。"), newIndex, context.Token)); }
}
public sealed class BookmarkFolderNodeSource(ScriptAccessContext context, BookmarkNode node)
{
    public string Name { get => context.Read(() => node.DisplayName); set => context.Run(() => context.State.RenameBookmarkAsync(node, value, context.Token)); }
    public string Path => context.Read(() => { using var list = new BookmarkFolderList(context.State.Bookmarks); return list.GetTargetPath(node); });
}

internal static class ScriptTreeParameters
{ public static string? String(IDictionary<string, object?>? parameter, string key) => parameter is not null && parameter.TryGetValue(key, out var value) ? value as string : null; }
