// Copyright (c) NeeLaboratory. 原 BookPageCollection.GetNextFolderIndex/GetPrevFolderIndex，基线 c5c398d89。
namespace NeeView;

/// <summary>沿用原页面集合的目录分组导航，其他索引和排序仍由原 BookPageSort 维护。</summary>
public sealed class BookPageCollection(IEnumerable<Page> pages) : List<Page>(pages)
{
    public PageSortMode SortMode { get; internal set; }
    /// <summary>文件名排序从下一页扫描，遇到目录变化返回第一项；末尾不循环。</summary>
    /// <param name="start">当前显示范围的最小归一索引。</param>
    /// <returns>下一组首项；排序不支持或已到尾组时返回 -1。</returns>
    public int GetNextFolderIndex(int start)
    {
        if (Count == 0 || !SortMode.IsFileNameCategory() || start < 0 || start >= Count) return -1;
        string current = GetDirectoryName(this[start].EntryName);
        for (int index = start + 1; index < Count; ++index)
            if (current != GetDirectoryName(this[index].EntryName)) return index;
        return -1;
    }
    /// <summary>从当前页前一项的目录反向找到首项；组内先回本组首项，组首才回上一组。</summary>
    /// <param name="start">当前显示范围的最小归一索引。</param>
    /// <returns>目标组首项；索引0或不支持排序返回 -1。</returns>
    public int GetPrevFolderIndex(int start)
    {
        if (Count == 0 || !SortMode.IsFileNameCategory() || start <= 0 || start >= Count) return -1;
        string current = GetDirectoryName(this[start - 1].EntryName);
        for (int index = start - 1; index > 0; --index)
            if (current != GetDirectoryName(this[index - 1].EntryName)) return index;
        return 0;
    }
    /// <summary>归档条目已规范成 '/'，目录文件名保持 Mac 的大小写与合法反斜杠。</summary>
    private static string GetDirectoryName(string entryName)
    { int index = entryName.LastIndexOf('/'); return index < 0 ? "" : entryName[..index]; }
}
