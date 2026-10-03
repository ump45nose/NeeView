// Copyright (c) NeeLaboratory. 来源：SearchBookmarkFolderCollection 和 FolderItem.GetValue。
using NeeLaboratory.IO.Search;
namespace NeeView;

/// <summary>原结构化查询适配到不可变节点快照；仅把原节点引用作为结果，不复制持久化书签。</summary>
public static class SearchBookmarkFolderCollection
{
    /// <summary>创建原书签 Profile；缓存只属于单次请求，避免常驻字符串无限累积。</summary>
    public static Searcher CreateSearcher() => new(new SearchContext().AddProfile(new DateSearchProfile()).AddProfile(new SizeSearchProfile()).AddProfile(new BookSearchProfile()));

    /// <summary>先解析有效语法，错误抛出；不使用原 Search(string) 的异常返回全量路径。</summary>
    /// <param name="keyword">搜索框 Trim 后的原表达式。</param><returns>原解析器的查询键。</returns>
    public static IReadOnlyList<SearchKey> Analyze(string keyword)
    {
        using var searcher = CreateSearcher(); return searcher.Analyze(keyword);
    }

    /// <summary>按原节点树序列过滤，日期/大小仅在实际用到时探测；结果不按相关度排序。</summary>
    /// <param name="keyword">已确认表达式。</param><param name="items">在调用线程捕获的只读名称/属性/身份。</param>
    /// <param name="metadata">后台文件系统能力，不存在返回空。</param><param name="token">替换请求或关闭取消。</param>
    /// <returns>属于原树的匹配节点；不修改树或正文。</returns>
    public static async Task<IReadOnlyList<BookmarkNode>> SearchAsync(string keyword, IReadOnlyList<BookmarkSearchItem> items,
        Func<string, CancellationToken, Task<FolderItem?>>? metadata, CancellationToken token)
    {
        // 查询解析和正则执行均在调用方后台任务中；无 UI 对象或长持有文件句柄。
        using var searcher = CreateSearcher(); var keys = searcher.Analyze(keyword);
        var needMetadata = keys.Any(key => key.Property.Name is "date" or "size");
        if (needMetadata)
        {
            if (metadata is null) throw new NotSupportedException("当前来源未提供日期/大小查询能力。");
            var seen = new Dictionary<string, FolderItem?>(StringComparer.Ordinal);
            foreach (var item in items.Where(item => !item.IsFolder && !string.IsNullOrWhiteSpace(item.Path)))
            {
                token.ThrowIfCancellationRequested();
                if (!seen.TryGetValue(item.Path!, out var info)) seen[item.Path!] = info = await metadata(item.Path!, token).ConfigureAwait(false);
                item.Metadata = info;
            }
        }
        var matches = searcher.Search(keys, items, token).Cast<BookmarkSearchItem>().Select(item => item.Node).ToHashSet();
        // 原 EntryTime 用节点的树索引排序；Union 的枚举次序不能改变注册次序。
        return items.Where(item => matches.Contains(item.Node)).Select(item => item.Node).ToArray();
    }
}

/// <summary>查询专用快照；名称/日期/历史属性固定，结果仍返回同一原节点。</summary>
public sealed class BookmarkSearchItem(BookmarkNode node, string name, string? path, bool isFolder, DateTime entryTime, bool inHistory) : ISearchItem
{
    public BookmarkNode Node { get; } = node;
    public string? Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public FolderItem? Metadata { get; set; }

    /// <summary>原 FolderItem.GetValue 的五种书签属性；调用时检查取消以中断长筛选。</summary>
    /// <param name="profile">原 Profile 属性。</param><param name="parameter">保留原参数入口。</param><param name="token">本次请求取消。</param>
    /// <returns>原 SearchValue；缺失文件沿原默认时间/大小。</returns>
    public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return profile.Name switch
        {
            "text" => new StringSearchValue(name),
            "bookmark" => new BooleanSearchValue(!IsFolder),
            "history" => new BooleanSearchValue(inHistory),
            "date" => new DateTimeSearchValue(IsFolder ? entryTime : Metadata?.LastWriteTime ?? default),
            "size" => new IntegerSearchValue(IsFolder ? -1 : Metadata?.Length ?? 0),
            _ => throw new NotSupportedException($"尚未支持书签属性：{profile.Name}")
        };
    }
}
