// Copyright (c) NeeLaboratory. 原 FolderOrderClass/GetFolderOrderMap 迁入；文案交由表现层。
namespace NeeView;

/// <summary>保留原来源排序资格：快速访问、普通目录、普通搜索、书签。</summary>
public enum FolderOrderClass { None, Normal, WithPath, Full }

public static class FolderOrderClassExtension
{
    private static readonly IReadOnlyList<FolderOrder> Full = Array.AsReadOnly(Enum.GetValues<FolderOrder>());
    private static readonly IReadOnlyList<FolderOrder> WithPath = Array.AsReadOnly(Full.Where(e => !e.IsEntryCategory()).ToArray());
    private static readonly IReadOnlyList<FolderOrder> Normal = Array.AsReadOnly(WithPath.Where(e => !e.IsPathCategory()).ToArray());
    private static readonly IReadOnlyList<FolderOrder> None = Array.AsReadOnly(new[] { FolderOrder.FileName });

    /// <summary>按原枚举顺序返回来源支持的排序，不包含界面类型或标签。</summary>
    /// <param name="self">当前已提交来源类别。</param><returns>不可修改的原资格表。</returns>
    public static IReadOnlyList<FolderOrder> GetFolderOrders(this FolderOrderClass self) => self switch
    { FolderOrderClass.Full => Full, FolderOrderClass.WithPath => WithPath, FolderOrderClass.Normal => Normal, _ => None };
}
