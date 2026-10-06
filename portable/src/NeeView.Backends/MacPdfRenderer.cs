using CoreGraphics;
using Foundation;
using PdfKit;
using System.Runtime.InteropServices;
using NeeView;
namespace NeeView.Backends;

/// <summary>官方CoreGraphics/PDFKit绑定替换Windows PDFium；原生文档/页面/上下文均为请求级。</summary>
public sealed class MacPdfRenderer(string? password = null) : IPdfRenderer
{
    private const long WorkingBudget = 256L * 1024 * 1024;
    public IPdfRenderer WithPassword(string key) => new MacPdfRenderer(key);
    /// <summary>只读页尺寸和目录，不渲染像素；返回纯数据后释放全部原生引用。</summary>
    /// <param name="path">真实PDF或应用拥有的嵌套代理。</param><param name="token">索引取消。</param>
    /// <returns>原页序/旋转尺寸/书签关系。</returns>
    public PdfDocumentInfo Inspect(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); using var pool = new NSAutoreleasePool(); using var document = Open(path);
        if (document.Pages > 100_000) throw new NotSupportedException("PDF超过十万页索引预算。");
        var pages = new List<Size>();
        for (var i = 1; i <= document.Pages; i++)
        {
            token.ThrowIfCancellationRequested(); using var page = document.GetPage(i) ?? throw new InvalidDataException("PDF页面缺失。");
            var bounds = page.GetBoxRect(CGPDFBox.Crop); var size = new Size((double)bounds.Width, (double)bounds.Height);
            if (Math.Abs(page.RotationAngle % 180) == 90) size = new(size.Height, size.Width);
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) throw new InvalidDataException("PDF页面尺寸无效。");
            pages.Add(size);
        }
        using var url = NSUrl.FromFilename(path); using var outlines = new PdfDocument(url);
        if (outlines.IsLocked && !outlines.Unlock(password ?? "")) throw new ArchiveKeyRequiredException();
        using var root = outlines.OutlineRoot; int nodes = 0;
        var contents = root is null ? [] : ReadOutline(root, 0);
        var attributes = new PdfDocumentAttributes(outlines.DocumentAttributes);
        using var modified = attributes.ModificationDate; using var created = attributes.CreationDate;
        return new(pages, contents, modified is null ? default : (DateTime)modified, created is null ? default : (DateTime)created);

        List<PdfOutlineInfo> ReadOutline(PdfOutline parent, int depth)
        {
            if (depth > 64) throw new NotSupportedException("PDF目录超过64层预算。");
            var result = new List<PdfOutlineInfo>();
            for (nint i = 0; i < parent.ChildrenCount; i++)
            {
                token.ThrowIfCancellationRequested(); if (++nodes > 100_000) throw new NotSupportedException("PDF目录超过十万项预算。");
                using var node = parent.Child(i); if (node is null) continue;
                using var destination = node.Destination;
                using var page = destination?.Page;
                var index = page is null ? -1 : (int)outlines.GetPageIndex(page);
                result.Add(new(node.Label ?? "", index >= 0 && index < pages.Count ? index : null, ReadOutline(node, depth + 1)));
            }
            return result;
        }
    }
    /// <summary>按请求大小绘制白色纸张、CropBox与页面旋转，直接输出sRGB预乘BGRA8；不经过PNG或delegate。</summary>
    /// <param name="path">实际来源文件。</param><param name="page">原零起始页面ID。</param>
    /// <param name="target">按原尺寸规则计算的实际输出规格。</param><param name="token">渲染前后取消。</param>
    /// <returns>唯一像素所有者；调用方归还缓存租约。</returns>
    public DecodedImageLease Render(string path, int page, Size target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(target.Width) || !double.IsFinite(target.Height) || target.Width < 1 || target.Height < 1
            || target.Width > int.MaxValue || target.Height > int.MaxValue) throw new NotSupportedException("PDF渲染尺寸无效或超过安全预算。");
        var width = Math.Max(1, (int)target.Width); var height = Math.Max(1, (int)target.Height);
        var bytes = checked((long)width * height * 4);
        if (bytes > WorkingBudget) throw new NotSupportedException("PDF渲染超过256MiB工作预算，请降低页面或导出尺寸。");
        using var pool = new NSAutoreleasePool(); using var document = Open(path);
        if (page < 0 || page >= document.Pages) throw new InvalidDataException("PDF页面不存在。");
        using var pdfPage = document.GetPage(page + 1) ?? throw new InvalidDataException("PDF页面不存在。");
        var pixels = new byte[(int)bytes]; var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var color = CGColorSpace.CreateSrgb();
            using var context = new CGBitmapContext(pinned.AddrOfPinnedObject(), width, height, 8, checked(width * 4), color,
                CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
            context.SetFillColor(1, 1, 1, 1); context.FillRect(new(0, 0, width, height));
            context.ConcatCTM(pdfPage.GetDrawingTransform(CGPDFBox.Crop, new(0, 0, width, height), 0, true));
            context.DrawPDFPage(pdfPage); token.ThrowIfCancellationRequested();
            return new(new(width, height), pixels);
        }
        finally { pinned.Free(); }
    }
    /// <summary>损坏/密码失败明确返回；不得让锁定文档伪装为空书。</summary>
    private CGPDFDocument Open(string path)
    {
        var document = CGPDFDocument.FromFile(path) ?? throw new InvalidDataException("PDF损坏或无法读取。");
        try
        {
            if (!document.IsUnlocked && !document.Unlock(password ?? "")) throw new ArchiveKeyRequiredException();
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
