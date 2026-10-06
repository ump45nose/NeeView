// Copyright (c) NeeLaboratory. 原 BookAddress.GetPlace/GetEntryName 的已支持来源子集。
namespace NeeView;

/// <summary>书籍目标与父书位置；不同于独立书架的当前浏览位置。</summary>
public sealed record BookAddress(string TargetPath, string? Place)
{
    /// <summary>保留原归档模式父级语义：包内递归来源回到根归档所在目录，当前目录模式逐级返回。</summary>
    public static BookAddress Create(Archive source)
    {
        var mode = Config.Current.System.ArchiveRecursiveMode;
        // 原GetPlace：IncludeSubDirectories的内部归档返回父归档，不跳到根文件所在目录。
        if (!source.IsDirectory && mode == ArchiveEntryCollectionMode.IncludeSubDirectories && source.Parent is { } parent)
            return new(source.Path, parent.Path);
        var path = !source.IsDirectory && mode != ArchiveEntryCollectionMode.CurrentDirectory ? source.RootArchivePath : source.Path;
        return new(source.Path, System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)));
    }
    /// <summary>相对父书的真实子项名称，用于父书定位，不能用书架索引代替。</summary>
    public string? ParentEntryName => Place is { } parent ? System.IO.Path.GetRelativePath(parent, TargetPath) : null;
}
