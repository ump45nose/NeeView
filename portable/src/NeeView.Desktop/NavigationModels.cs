using System.Collections.ObjectModel;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>目录树表现节点，无控件引用；目录访问策略由工作区负责。</summary>
public sealed class FolderNode(string path)
{
    public string Path { get; } = path;
    public ObservableCollection<FolderNode> Children { get; } = [];
    public bool Loaded { get; set; }
    /// <summary>将定位路径转换成节点标题，根目录仍可识别。</summary>
    public override string ToString() => System.IO.Path.GetFileName(Path.TrimEnd('/')) is { Length: > 0 } name ? name : Path;
}
public sealed record NavigationData(IReadOnlyList<ReadingState> History, IReadOnlyList<BookmarkNode> Bookmarks, FolderNode? Root);
public sealed record DestinationData(IReadOnlyList<string> Managed, IReadOnlyList<string> Children);
