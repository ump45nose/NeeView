// Copyright (c) NeeLaboratory. 移植自 SidePanels/History/HistoryList.cs，沿用仓库 MIT。
namespace NeeView;

/// <summary>原历史列表的过滤与前后浏览规则；不依赖 WPF CollectionView 或界面选择。</summary>
public sealed class HistoryList(SaveData saveData)
{
    public string SearchKeyword { get; set; } = "";
    public string? Address { get; set; }
    public string? FilterPath => Config.Current.History.IsCurrentFolder && Address is not null ? System.IO.Path.GetDirectoryName(Address) : null;

    /// <summary>按原访问倒序过滤直接父目录；文本搜索沿用当前子集，结构化搜索后续迁入。</summary>
    /// <returns>只读展示序列；不改动权威历史、排序或源文件。</returns>
    public IReadOnlyList<HistoryEntry> GetViewItems() => saveData.HistoryEntries.Where(item =>
        (string.IsNullOrEmpty(FilterPath) || FilterPath == System.IO.Path.GetDirectoryName(item.Path)) &&
        item.Path.Contains(SearchKeyword, StringComparison.CurrentCultureIgnoreCase)).ToArray();

    /// <summary>保留原 PrevHistory/NextHistory：后退找较旧项，前进找较新项。</summary>
    /// <param name="direction">-1 为较旧，1 为较新；当前项不在筛选结果中时只有后退可选首项。</param>
    /// <returns>实际目标；到边界时为 null，不循环。</returns>
    public HistoryEntry? GetTarget(int direction)
    {
        var items = GetViewItems();
        var index = items.ToList().FindIndex(item => item.Path == Address);
        if (index < 0) return direction < 0 ? items.FirstOrDefault() : null;
        int target = index + (direction < 0 ? 1 : -1);
        return target >= 0 && target < items.Count ? items[target] : null;
    }

    /// <summary>原日期分组：今天、昨天或当地完整日期，时间不因分组而重写。</summary>
    /// <param name="time">历史原访问时间。</param>
    /// <param name="today">测试或表现端传入当前本地日期。</param>
    public static string GetGroupName(DateTime time, DateTime today)
    {
        var date = (time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time).Date;
        return date == today.Date ? "今天" : date == today.Date.AddDays(-1) ? "昨天" : date.ToString("D", System.Globalization.CultureInfo.CurrentCulture);
    }
}
