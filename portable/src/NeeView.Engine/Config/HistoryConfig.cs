// Copyright (c) NeeLaboratory. 来自原 HistoryConfig 的列表分支，其他字段保留在 JSON。
namespace NeeView;

/// <summary>原历史面板、搜索及文件保留策略；登记、清理与文件保留策略。</summary>
public sealed class HistoryConfig
{
    public PanelListItemStyle PanelListItemStyle { get; set; } = PanelListItemStyle.Content;
    public bool IsSaveHistory { get; set; } = true;
    public bool IsForceUpdateHistory { get; set; }
    public int HistoryEntryPageCount { get; set; }
    public bool IsInnerArchiveHistoryEnabled { get; set; } = true;
    public bool IsUncHistoryEnabled { get; set; } = true;
    public bool IsAutoCleanupEnabled { get; set; }
    private int _limitSize = -1;
    private TimeSpan _limitSpan;
    /// <summary>原保存数量限制；-1无限，0不保存访问条目，运行中集合不受限。</summary>
    public int LimitSize { get => _limitSize; set => _limitSize = Math.Max(value, -1); }
    /// <summary>原保留期限；零无限，负值按原setter归零。</summary>
    public TimeSpan LimitSpan { get => _limitSpan; set => _limitSpan = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
    private int _recentBookCount = 10;
    /// <summary>原最近打开书籍菜单上限，至少一项；不裁剪历史权威集合。</summary>
    public int RecentBookCount { get => _recentBookCount; set => _recentBookCount = Math.Max(1,value); }
    public bool IsCurrentFolder { get; set; }
    public bool IsGroupBy { get; set; }
    public bool IsVisibleItemsCount { get; set; } = true;
    public bool IsVisibleSearchBox { get; set; } = true;
    public bool IsKeepSearchHistory { get; set; } = true;
    public bool IsKeepFolderStatus { get; set; } = true;
}
