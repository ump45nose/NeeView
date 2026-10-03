// Copyright (c) NeeLaboratory. 来源：BookHistory.GetValue 与 HistoryList.Filter/UpdateFilter，沿用仓库 MIT。
using NeeLaboratory.IO.Search;
namespace NeeView;

/// <summary>原历史列表查询的后台适配；不复用书签的树遍历或文件修改时间。</summary>
public static class SearchHistoryCollection
{
    /// <summary>解析原历史表达式；错误传播，禁止回退全量并冒充搜索成功。</summary>
    /// <param name="keyword">已Trim的输入。</param><returns>原查询属性键，供按需大小探测与状态校验。</returns>
    public static IReadOnlyList<SearchKey> Analyze(string keyword)
    { using var searcher = BookSearchProfile.CreateSearcher(); return searcher.Analyze(keyword); }

    /// <summary>沿原逐项SearcherFilter过滤，保持访问倒序；只有大小查询读取文件元数据。</summary>
    /// <param name="keyword">已解析的原表达式。</param><param name="items">调用线程捕获的历史、书签标志快照。</param>
    /// <param name="metadata">后台来源接口；不存在返回null。</param><param name="token">本次需求取消。</param>
    /// <returns>匹配的真实书籍路径；不修改权威历史或阅读状态。</returns>
    public static async Task<HashSet<string>> SearchAsync(string keyword, IReadOnlyList<HistorySearchItem> items,
        Func<string, CancellationToken, Task<FolderItem?>>? metadata, CancellationToken token)
    {
        using var searcher = BookSearchProfile.CreateSearcher();
        var keys = searcher.Analyze(keyword); var filter = searcher.CreateFilter(keyword);
        if (keys.Any(key => key.Property.Name == "size"))
        {
            if (metadata is null) throw new NotSupportedException("当前来源未提供历史大小查询能力。");
            var seen = new Dictionary<string, FolderItem?>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (!seen.TryGetValue(item.Entry.Path, out var info)) seen[item.Entry.Path] = info = await metadata(item.Entry.Path, token).ConfigureAwait(false);
                item.Length = info is { IsDirectory: false } ? info.Length : -1;
            }
        }
        // 原历史使用逐项布尔Filter，而非书签集合Union；保留 /or、/not 的原判断顺序。
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        { token.ThrowIfCancellationRequested(); if (filter.Filter(item)) result.Add(item.Entry.Path); }
        return result;
    }
}

/// <summary>原BookHistory.GetValue的不可变输入；文件大小只属于本次查询。</summary>
public sealed class HistorySearchItem(HistoryEntry entry, bool isBookmark) : ISearchItem
{
    public HistoryEntry Entry { get; } = entry;
    public bool IsBookmark { get; } = isBookmark;
    public long Length { get; set; } = -1;

    /// <summary>映射原五属性：书名、访问时间、真实文件大小、书签成员、恒为真的历史标志。</summary>
    /// <param name="profile">原Profile。</param><param name="parameter">保留原属性参数入口。</param><param name="token">查询取消。</param>
    /// <returns>原SearchValue；目录与缺失来源大小均为-1。</returns>
    public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return profile.Name switch
        {
            "text" => new StringSearchValue(Entry.Name),
            "date" => new DateTimeSearchValue(Entry.LastAccessTime),
            "size" => new IntegerSearchValue(Length),
            "bookmark" => new BooleanSearchValue(IsBookmark),
            "history" => new BooleanSearchValue(true),
            _ => throw new NotSupportedException($"尚未支持历史属性：{profile.Name}")
        };
    }
}
