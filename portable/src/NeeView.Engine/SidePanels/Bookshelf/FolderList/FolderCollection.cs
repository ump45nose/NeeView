// Copyright (c) NeeLaboratory. 从原 FolderCollection.Sort/ComparerFileType 迁入普通文件系统排序，不包含 WPF 图标及监视器。
namespace NeeView;

/// <summary>普通书架集合排序；业务次序与面板显示、前后书导航共用。</summary>
public static class FolderCollection
{
    /// <summary>按原先目录分组、再指定键、最后自然文件名排序；随机在分组内使用稳定种子。</summary>
    /// <param name="source">后端只读元数据。</param>
    /// <param name="mode">原排序枚举。</param>
    /// <param name="folderSort">目录置前、置后或混排。</param>
    /// <param name="seed">当前书架目录的随机种子。</param>
    /// <param name="token">取消排序。</param>
    /// <returns>与原普通书架相同的有序条目。</returns>
    public static IReadOnlyList<FolderItem> Sort(IEnumerable<FolderItem> source, FolderOrder mode, FolderSortOrder folderSort, int seed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // 当前普通目录没有系统/父目录条目；原 Type.ConstOrder 的这一组同为常量。
        var order = source.OrderBy(_ => 0);
        order = folderSort switch
        {
            FolderSortOrder.First => order.ThenBy(e => e.IsDirectory ? 0 : 1),
            FolderSortOrder.Last => order.ThenByDescending(e => e.IsDirectory ? 0 : 1),
            _ => order
        };
        var byName = Comparer<FolderItem>.Create((x, y) => { token.ThrowIfCancellationRequested(); return NaturalSort.Compare(x.Name, y.Name); });
        var byPath = Comparer<FolderItem>.Create((x, y) => { token.ThrowIfCancellationRequested(); return NaturalSort.Compare(x.Path, y.Path); });
        var byType = Comparer<FolderItem>.Create((x, y) => CompareFileType(x, y, token));
        var random = new Random(seed);
        var sorted = mode switch
        {
            FolderOrder.FileNameDescending => order.ThenByDescending(e => e, byName),
            FolderOrder.Path => order.ThenBy(e => e, byPath),
            FolderOrder.PathDescending => order.ThenByDescending(e => e, byPath),
            FolderOrder.FileType => order.ThenBy(e => e, byType),
            FolderOrder.FileTypeDescending => order.ThenByDescending(e => e, byType),
            FolderOrder.TimeStamp => order.ThenBy(e => e.LastWriteTime).ThenBy(e => e, byName),
            FolderOrder.TimeStampDescending => order.ThenByDescending(e => e.LastWriteTime).ThenBy(e => e, byName),
            FolderOrder.Size => order.ThenBy(e => e.Length).ThenBy(e => e, byName),
            FolderOrder.SizeDescending => order.ThenByDescending(e => e.Length).ThenBy(e => e, byName),
            FolderOrder.Random => order.ThenBy(_ => random.Next()),
            _ => order.ThenBy(e => e, byName)
        };
        try { var result = sorted.ToArray(); token.ThrowIfCancellationRequested(); return result; }
        catch (InvalidOperationException ex) when (ex.InnerException is OperationCanceledException cancelled) { throw cancelled; }
    }

    /// <summary>保留原目录不比较扩展名、文件先扩展名后自然名称的规则。</summary>
    private static int CompareFileType(FolderItem x, FolderItem y, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (x.IsDirectory) return y.IsDirectory ? NaturalSort.Compare(x.Name, y.Name) : 1;
        if (y.IsDirectory) return -1;
        var extX = System.IO.Path.GetExtension(x.Name); var extY = System.IO.Path.GetExtension(y.Name);
        return extX != extY ? NaturalSort.Compare(extX, extY) : NaturalSort.Compare(x.Name, y.Name);
    }
}
