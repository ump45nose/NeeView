// Copyright (c) NeeLaboratory. 原PageSearchProfile和Page.GetValue查询规则。
using NeeLaboratory.IO.Search;
namespace NeeView;
public sealed class PageSearchProfile : SearchProfile
{
    public PageSearchProfile()
    {
        Options.Add(new SearchPropertyProfile("playlist", BooleanSearchValue.Default)); Options.Add(new SearchPropertyProfile("meta", StringSearchValue.Default)); Options.Add(new SearchPropertyProfile("rating", IntegerSearchValue.Default));
        Alias.Add("/playlist", new() { "/p.playlist", "/m.eq", "true" });
        foreach (var name in new[] { "title", "subject", "tags", "comments" }) Alias.Add("/" + name, new() { "/p.meta." + name });
        Alias.Add("/rating", new() { "/p.rating" });
    }
    public static Searcher CreateSearcher() => new(new SearchContext().AddProfile(new DateSearchProfile()).AddProfile(new SizeSearchProfile()).AddProfile(new PageSearchProfile()));
    /// <summary>先校验语法和能力，坏查询不能覆盖当前可读页。</summary>
    public static IReadOnlyList<SearchKey> Analyze(string keyword)
    {
        using var searcher = CreateSearcher(); var keys = searcher.Analyze(keyword);
        if (keys.Any(key => key.Property.Name is "meta" or "rating")) throw new NotSupportedException("图片元数据和评分搜索尚未迁移，原表达式会保留到后续阶段。");
        return keys;
    }
    /// <summary>原_sourcePages → Searcher.Search → 排序；返回同一Page，不改变来源集合。</summary>
    public static IReadOnlyList<Page> Search(string keyword, IEnumerable<Page> source, CancellationToken token)
    {
        if (keyword.Length == 0) return source.ToArray();
        var keys = Analyze(keyword); using var searcher = CreateSearcher(); return searcher.Search(keys, source, token).Cast<Page>().ToArray();
    }
}
