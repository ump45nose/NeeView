// Copyright (c) NeeLaboratory. 原PdfArchive/PdfPdfiumArchive与ContentsArchiveEntryNode关系的Mac适配。
namespace NeeView;

/// <summary>原PDF页面来源；只包含尺寸/目录关系，不引用具体渲染库或窗口。</summary>
public abstract class PdfArchive(string path, ArchiveEntry? source = null) : Archive(path, source)
{
    /// <summary>返回原PDF页尺寸，区别于默认导出或按需显示像素。</summary>
    /// <param name="entry">该来源的实际页面条目。</param><returns>旋转校正后的页面尺寸。</returns>
    public abstract Size GetSourceSize(ArchiveEntry entry);
}
/// <summary>原PDF书签目录节点，关联真实条目，不创建第二页面集合。</summary>
public sealed record ContentsArchiveEntryNode(string Name, ArchiveEntry? ArchiveEntry, IReadOnlyList<ContentsArchiveEntryNode> Children);
