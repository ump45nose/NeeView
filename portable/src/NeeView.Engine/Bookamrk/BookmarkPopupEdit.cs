// Copyright (c) NeeLaboratory. 来源：NeeView/AddressBar/BookmarkPopupEdit.cs。
namespace NeeView;

public enum BookmarkPopupResult { None, Add, Remove, Edit }

/// <summary>原登记编辑上下文；移除 WPF/QueryPath，保留名称、同父查找及 Add/Edit/Remove 的区别。</summary>
public sealed class BookmarkPopupEdit
{
    private string _name;
    public BookMemento Memento { get; }
    public BookmarkNode? Node { get; }
    public bool IsEdit => Node is not null;
    public string Name { get => _name; set { var name = BookmarkCollection.ValidateName(value); _name = name.Length == 0 ? DefaultName : name; } }
    private string DefaultName => System.IO.Path.GetFileName(Memento.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    /// <summary>优先当前选择的同书节点，否则查询所选父级；不跨文件夹误编辑同书记录。</summary>
    public BookmarkPopupEdit(BookMemento memento, BookmarkNode parent, BookmarkNode? node)
    {
        Memento = memento;
        Node = node is { IsFolder: false } && node.Path == memento.Path ? node : parent.Children!.FirstOrDefault(e => !e.IsFolder && e.Path == memento.Path);
        _name = Node?.DisplayName ?? DefaultName;
    }
}
