// Copyright (c) NeeLaboratory. 来自原 HistoryConfig 的列表分支，其他字段保留在 JSON。
namespace NeeView;

/// <summary>原历史面板和搜索开关；显示样式及保留策略尚未迁入。</summary>
public sealed class HistoryConfig
{
    public bool IsCurrentFolder { get; set; }
    public bool IsGroupBy { get; set; }
    public bool IsVisibleItemsCount { get; set; } = true;
    public bool IsVisibleSearchBox { get; set; } = true;
    public bool IsKeepSearchHistory { get; set; } = true;
}
