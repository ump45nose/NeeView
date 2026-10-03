// Copyright (c) NeeLaboratory. 迁入原 BookHistoryCollection.Limit 子集。
namespace NeeView;

/// <summary>原历史集合的文件保留算法；运行中的访问/阅读状态仍由唯一SaveData管理。</summary>
public static class BookHistoryCollection
{
    /// <summary>按原顺序先限制数量，再取严格晚于截止时间的连续前缀。</summary>
    /// <typeparam name="T">原历史条目或保留未知字段的JSON节点。</typeparam>
    /// <param name="source">已按访问时间倒序的历史。</param><param name="limitSize">-1无限，其他值取前N项。</param>
    /// <param name="limitSpan">零无限；调用方使用原HistoryConfig归一负值。</param>
    /// <param name="lastAccessTime">仅替代原IBookHistory约束，不改变比较规则。</param>
    /// <param name="now">可选固定本地时钟，用于边界验证。</param>
    /// <returns>延迟执行的保留序列，不修改来源或运行中集合。</returns>
    public static IEnumerable<T> Limit<T>(IEnumerable<T> source, int limitSize, TimeSpan limitSpan,
        Func<T, DateTime> lastAccessTime, DateTime? now = null)
    {
        // 原数量优先，不以逐项Where替换遇到首个过期记录就终止的TakeWhile。
        var collection = limitSize == -1 ? source : source.Take(limitSize);
        if (limitSpan == default) return collection;
        var time = now ?? DateTime.Now;
        // TimeSpan允许超出DateTime范围；极大旧值使用最早日期，避免阻止正常保存。
        var limitTime = limitSpan.Ticks > time.Ticks ? DateTime.MinValue : time - limitSpan;
        return collection.TakeWhile(item => lastAccessTime(item) > limitTime);
    }
}
