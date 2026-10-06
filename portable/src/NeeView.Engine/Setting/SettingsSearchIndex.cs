// Copyright (c) NeeLaboratory. SettingWindowModel.SettingItemRecord 与原 Searcher 的无 UI 适配。
using NeeLaboratory.IO.Search;
namespace NeeView;

/// <summary>原设置搜索记录；目标键由表现端定位同一个编辑草稿，不承载配置副本。</summary>
/// <param name="Target">当前窗口内稳定的设置或命令目标键。</param>
/// <param name="Page">页面标题。</param><param name="Section">分区标题及提示。</param>
/// <param name="Text">设置标题、提示或命令参数文案。</param>
public sealed record SettingsSearchItem(string Target, string Page, string Section, string Text) : ISearchItem
{
    /// <summary>沿用原 Page + Section + Item 文本，只支持原默认 text 属性。</summary>
    /// <param name="profile">原搜索属性。</param><param name="parameter">原属性参数。</param>
    /// <param name="token">查询取消。</param><returns>原字符串搜索值。</returns>
    public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return profile.Name == "text" ? new StringSearchValue(Page + " " + Section + " " + Text)
            : throw new NotSupportedException($"Not supported SearchProperty: {profile.Name}");
    }
}

/// <summary>复用原结构化搜索器；不增加配置、历史持久化或另一种匹配规则。</summary>
public sealed class SettingsSearchIndex(IEnumerable<SettingsSearchItem> items) : IDisposable
{
    private readonly Searcher _searcher = new(new SearchContext());
    private readonly SettingsSearchItem[] _items = items.ToArray();
    /// <summary>窗口内的完整可搜索能力目录，包含禁用占位命令。</summary>
    public IReadOnlyList<SettingsSearchItem> Items => _items;
    /// <summary>输入前校验原语法；解析错误不能降级为全部匹配。</summary>
    /// <param name="keyword">trim 后的表达式。</param>
    public void Analyze(string keyword) => _searcher.Analyze(keyword);
    /// <summary>按原索引顺序返回匹配项；先解析再搜索，显式传播错误和取消。</summary>
    /// <param name="keyword">原搜索表达式。</param><param name="token">窗口查询取消。</param>
    /// <returns>只读查询结果，不修改任何设置。</returns>
    public IReadOnlyList<SettingsSearchItem> Search(string keyword, CancellationToken token = default)
    {
        var keys = _searcher.Analyze(keyword.Trim());
        return _searcher.Search(keys, _items, token).Cast<SettingsSearchItem>().ToArray();
    }
    /// <summary>关闭窗口释放原搜索器。</summary>
    public void Dispose() => _searcher.Dispose();
}
