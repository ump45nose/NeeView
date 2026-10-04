// Copyright (c) NeeLaboratory. 原FileSearchContext/FileItem.GetValue及FolderSearchCollection筛选顺序。
using NeeLaboratory.IO.Search;
namespace NeeView;
public static class SearchBookshelfCollection
{
    public static void Analyze(string keyword) { using var searcher = BookSearchProfile.CreateSearcher(); searcher.Analyze(keyword); }
    /// <summary>同一元数据快照经过原Searcher，保留名称/修改时间/大小/书签/历史真实属性。</summary>
    public static IReadOnlyList<FolderItem> Search(string keyword, IReadOnlyList<BookshelfSearchItem> items, CancellationToken token)
    {
        using var searcher = BookSearchProfile.CreateSearcher(); var keys = searcher.Analyze(keyword);
        return searcher.Search(keys, items, token).Cast<BookshelfSearchItem>().Select(item => item.Entry).ToArray();
    }
}
public sealed class BookshelfSearchItem(FolderItem entry, bool bookmark, bool history) : ISearchItem
{
    public FolderItem Entry { get; } = entry;
    public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return profile.Name switch { "text" => new StringSearchValue(Entry.Name), "date" => new DateTimeSearchValue(Entry.LastWriteTime),
            "size" => new IntegerSearchValue(Entry.Length), "bookmark" => new BooleanSearchValue(bookmark), "history" => new BooleanSearchValue(history),
            _ => throw new NotSupportedException($"书架搜索属性尚未迁移：{profile.Name}") };
    }
}
