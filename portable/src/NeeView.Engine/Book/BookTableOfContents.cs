// Copyright (c) NeeLaboratory. 原BookTableOfContents/ContentsPageNode.Add及GetPagesPrefix。
namespace NeeView;
public enum PageNameFormat { Smart, NameOnly, Raw, PageNumber }
/// <summary>目录节点引用原Page，点击只跳页，不过滤正文或生成新Page。</summary>
public sealed class ContentsPageNode
{
    public bool IsRoot { get; init; }
    public bool IsExpanded { get; set; }
    public string Name { get; init; } = "";
    public Page? Page { get; init; }
    public List<ContentsPageNode> Children { get; } = [];
    /// <summary>沿原逐段插入，中间目录也引用首次插入分支的代表页。</summary>
    /// <param name="page">目录首次出现时的原来源代表页。</param><param name="path">以'/'分隔的逻辑目录。</param>
    public void Add(Page page, string path)
    {
        var current = this;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var child = current.Children.FirstOrDefault(n => n.Name == part);
            if (child is null) { child = new() { Name = part, Page = page }; current.Children.Add(child); }
            current = child;
        }
    }
}
public static class BookTableOfContents
{
    /// <summary>取得逻辑条目的直接目录，无目录时返回空字符串。</summary>
    /// <param name="path">原来源条目名。</param><returns>不含尾分隔符的目录名。</returns>
    public static string DirectoryName(string path) => path.LastIndexOf('/') is var index && index >= 0 ? path[..index] : "";
    /// <summary>原来源页临时FileName排序，不以搜索结果或当前反序构造目录树。</summary>
    /// <param name="source">未过滤来源页快照。</param><param name="token">后台排序和建树取消。</param><returns>引用原Page的目录根。</returns>
    public static ContentsPageNode Create(IReadOnlyList<Page> source, CancellationToken token, Archive? archive = null)
    {
        var pages = BookPageSort.Sort(source, PageSortMode.FileName, 0, token).Pages;
        var root = new ContentsPageNode { IsRoot = true, IsExpanded = true, Name = "目录", Page = pages.FirstOrDefault() };
        if (archive?.Contents is { Count: > 0 } contents)
        {
            var targets = source.ToDictionary(p => p.ArchiveEntry.SystemPath, StringComparer.Ordinal);
            AddContents(root, contents); return root;
            void AddContents(ContentsPageNode parent, IReadOnlyList<ContentsArchiveEntryNode> nodes)
            {
                foreach (var node in nodes)
                {
                    token.ThrowIfCancellationRequested();
                    var child = new ContentsPageNode { Name = node.Name, Page = node.ArchiveEntry is { } entry ? targets.GetValueOrDefault(entry.SystemPath) : null };
                    parent.Children.Add(child); AddContents(child, node.Children);
                }
            }
        }
        foreach (var group in pages.GroupBy(p => DirectoryName(p.EntryName))) { token.ThrowIfCancellationRequested(); root.Add(group.First(), group.Key); }
        return root;
    }
    /// <summary>原最长公共字符前缀截到最后目录边界，单目录页保留名称。</summary>
    /// <param name="pages">全源页面快照。</param><returns>带尾分隔符的公共目录或空字符串。</returns>
    public static string GetPagesPrefix(IReadOnlyList<Page> pages)
    {
        if (pages.Count == 0) return ""; string prefix = pages[0].EntryName;
        foreach (var page in pages) { int length = Math.Min(prefix.Length, page.EntryName.Length), i = 0; while (i < length && prefix[i] == page.EntryName[i]) i++; prefix = prefix[..i]; if (i == 0) break; }
        if (pages.Count == 1) prefix = prefix.TrimEnd('/');
        int index = prefix.LastIndexOf('/'); return index >= 0 ? prefix[..(index + 1)] : "";
    }
}
