// Copyright (c) NeeLaboratory. 原 FolderTreeNodeBase 的父子、展开及选择关系；移除 WPF 图标/文件操作。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
namespace NeeView;

/// <summary>原普通目录树节点关系，不持有控件、图像或窗口。</summary>
public abstract class FolderTreeNodeBase : ObservableObject, IDisposable
{
    private bool _disposed, _selected, _expanded;
    protected ObservableCollection<FolderTreeNodeBase>? _children;
    public FolderTreeNodeBase? Parent { get; protected init; }
    public FolderTreeNodeBase Root => Parent?.Root ?? this;
    public bool IsDisposed => _disposed || Parent?.IsDisposed == true;
    public virtual bool IsPlaceholder => false;
    public abstract string Name { get; }
    public virtual string Path => "";
    public virtual string DisplayName => Name;
    public virtual string DisplayText => DisplayName;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
    public virtual bool IsExpanded
    {
        get => _expanded;
        set { if (value && Parent is not null) Parent.IsExpanded = true; SetProperty(ref _expanded, value); }
    }
    public virtual ObservableCollection<FolderTreeNodeBase> Children => _children ?? [];
    public ObservableCollection<FolderTreeNodeBase>? ChildrenRaw => _children;
    /// <summary>按原父链验证节点归属，拒绝其他树或已经退役的节点。</summary>
    public bool ContainsRoot(FolderTreeNodeBase root) => !IsDisposed && (ReferenceEquals(this, root) || Parent?.ContainsRoot(root) == true);
    /// <summary>窗口/父节点关闭时递归释放请求和子引用，重复调用安全。</summary>
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true; IsSelected = false;
        OnPropertyChanged(nameof(IsDisposed));
        foreach (var child in _children ?? []) child.Dispose();
    }
}

/// <summary>原延迟节点的占位项，只给展开箭头，不枚举或参与选择/打开。</summary>
internal sealed class DummyNode : FolderTreeNodeBase
{ public override string Name => ""; public override bool IsPlaceholder => true; public string? Error => null; }
