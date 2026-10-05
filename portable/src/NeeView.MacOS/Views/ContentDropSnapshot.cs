using Avalonia.Input;
using Avalonia.Platform.Storage;
namespace NeeView.MacOS.Views;

/// <summary>拖入表现适配只取有限数据快照，不下载/枚举/落盘；业务接收由Engine转交。</summary>
public static class ContentDropSnapshot
{
    /// <summary>借用拖放对象读取快照；Bitmap仍属于发送者，不在此释放。</summary>
    /// <param name="transfer">本次拖放的有限数据。</param><returns>可交给唯一内容接收链路的快照。</returns>
    public static FileClipboardContent Read(IDataTransfer transfer)
    {
        var query = transfer.TryGetValue(DataFormat.CreateStringPlatformFormat(FileClipboardCodec.QueryPathsType));
        if (query is not null) return new([], FileClipboardCodec.DecodeQueryPaths(query));
        var droppedFiles = (transfer.TryGetFiles() ?? []).Take(FileClipboardCodec.MaximumItems + 1).ToArray();
        if (droppedFiles.Length > FileClipboardCodec.MaximumItems) throw new NotSupportedException("拖入文件数量超过支持上限。");
        var files = FileClipboardCodec.ValidatePaths(droppedFiles.Select(f => f.TryGetLocalPath()).OfType<string>());
        var html = transfer.TryGetValue(DataFormat.CreateStringPlatformFormat("public.html"));
        if (html?.Length > 1024 * 1024) throw new NotSupportedException("HTML超过1MiB预算。");
        var url = transfer.TryGetValue(DataFormat.CreateStringPlatformFormat("public.url"));
        var web = url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? new[] { url } : [];
        if (files.Length > 0 && files.Length != droppedFiles.Length && web.Length == 0)
            throw new NotSupportedException("拖入文件中存在尚未提供本机路径的项目，本次未打开部分文件。");
        if (files.Length > 0 && web.Length == 0) return new(files, []);
        var images = new List<ContentDropImage>();
        foreach (var type in new[] { "public.png", "public.tiff", "public.jpeg" })
        {
            var bytes = transfer.TryGetValue(DataFormat.CreateBytesPlatformFormat(type));
            if (bytes is null) continue;
            if (bytes.Length > 64 * 1024 * 1024) throw new NotSupportedException("拖入图片超出64MiB预算。");
            images.Add(new(bytes, IsBitmap: true)); break;
        }
        if (images.Count == 0 && transfer.TryGetBitmap() is { } bitmap)
        {
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4 > 128L * 1024 * 1024) throw new NotSupportedException("拖入位图超出像素预算。");
            using var buffer = new MemoryStream(); bitmap.Save(buffer, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); images.Add(new(buffer.ToArray(), IsBitmap: true));
        }
        var content = images.Count > 0 || html is not null || web.Length > 0 ? new ContentDropData(images, html, web) : null;
        // 浏览器可能同时提供尚未实体化的文件承诺与URL/图片；已有标准数据时继续业务接收。
        if (files.Length != droppedFiles.Length && content is null) throw new NotSupportedException("拖入文件尚未提供本机路径或图片/URL数据。");
        return new(files, [], Content: content);
    }
}
