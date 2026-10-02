// Copyright (c) NeeLaboratory. 原枚举及分类来自 FolderOrder.cs，基线 c5c398d89；仅移除 AliasName 属性。
namespace NeeView;

/// <summary>保留原 JSON 枚举顺序，普通目录不开放路径及注册时间排序。</summary>
public enum FolderOrder
{
    FileName, FileNameDescending, Path, PathDescending, FileType, FileTypeDescending,
    TimeStamp, TimeStampDescending, Size, SizeDescending, EntryTime, EntryTimeDescending, Random
}
public static class FolderOrderExtension
{
    /// <summary>判断原注册时间类别，仅在书签等具有注册顺序的来源使用。</summary>
    public static bool IsEntryCategory(this FolderOrder mode) => mode is FolderOrder.EntryTime or FolderOrder.EntryTimeDescending;
    /// <summary>判断原完整路径类别，普通单目录列表不提供此选项。</summary>
    public static bool IsPathCategory(this FolderOrder mode) => mode is FolderOrder.Path or FolderOrder.PathDescending;
    /// <summary>保留原六种降序枚举的判定。</summary>
    public static bool IsDescending(this FolderOrder mode) => mode is FolderOrder.FileNameDescending or FolderOrder.PathDescending
        or FolderOrder.FileTypeDescending or FolderOrder.TimeStampDescending or FolderOrder.SizeDescending or FolderOrder.EntryTimeDescending;
}
