// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView;

/// <summary>原 Archive 的 P1 读取边界，来源保留条目所属关系并负责释放。</summary>
public abstract class Archive(string path) : IAsyncDisposable
{
    public string Path { get; } = path;
    public virtual string RootArchivePath => Path;
    /// <summary>来源解析的显式图片条目，普通历史恢复不能覆盖该定位。</summary>
    public string? RequestedEntryName { get; init; }
    public virtual bool IsDirectory => false;
    public bool IsDisposed { get; protected set; }
    /// <summary>建立条目索引，后台执行且支持取消。</summary>
    public abstract Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token);
    /// <summary>打开指定条目，返回流所有权转交调用方。</summary>
    public abstract Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token);
    /// <summary>释放来源及应用生成的临时文件。</summary>
    public abstract ValueTask DisposeAsync();
}

/// <summary>原 ArchiveEntry 的只读子集，ID 区分归档中同名条目。</summary>
public sealed class ArchiveEntry(Archive archive)
{
    public Archive Archive { get; } = archive;
    public int Id { get; init; }
    public string RawEntryName { get; init; } = "";
    public string EntryName => Archive.IsDirectory ? RawEntryName : RawEntryName.Replace('\\', '/');
    public string Extension => System.IO.Path.GetExtension(EntryName);
    public long Length { get; init; }
    public DateTime LastWriteTime { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsShortcut { get; init; }
    public string? FilePath { get; init; }
    /// <summary>原 SystemPath：真实文件或归档加内部条目，绝不指向解压缓存。</summary>
    public string SystemPath => FilePath ?? System.IO.Path.Combine(Archive.Path, EntryName.TrimStart('/'));
    /// <summary>原已支持的图片候选；损坏图片仍保留页面。</summary>
    public bool IsImage() => !IsDirectory && ImageFormats.IsImage(EntryName);
    /// <summary>原书籍候选：目录或已接入的压缩格式。</summary>
    public bool IsBook() => IsDirectory || ArchiveFormats.IsArchive(EntryName);
    public override string ToString() => EntryName;
}
