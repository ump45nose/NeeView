using AppKit;
using Foundation;
using NeeView;
namespace NeeView.Backends;

/// <summary>NSPasteboard标准file URL与原QueryPath适配；不伪造Windows剪切标记，不访问内容文件。</summary>
public sealed class MacFileClipboard : IFileClipboard
{
    private const string FileUrlType = "public.file-url";
    private const string TextType = "public.utf8-plain-text";
    // 能力查询由UI菜单/输入调用；不在后台同步等待主线程，防止关闭互锁。
    public bool HasFileContent => NSThread.IsMain && NSPasteboard.GeneralPasteboard.Types?.Any(type => type == FileUrlType || type == FileClipboardCodec.QueryPathsType
        || type is "public.png" or "public.tiff" or "public.jpeg" or "public.html" or "public.url") == true;
    /// <summary>主线程创建完整原生对象后再清空系统剪贴板，提交点后不检查晚取消。</summary>
    /// <param name="content">业务核对过的有限实体和逻辑地址。</param><param name="token">排队/提交前取消。</param>
    /// <returns>写入成功任务；系统拒绝写入时抛出。</returns>
    public Task WriteAsync(FileClipboardContent content, CancellationToken token) => OnMainThreadAsync(() =>
    {
        var files = FileClipboardCodec.ValidatePaths(content.Files); var query = FileClipboardCodec.ValidatePaths(content.QueryPaths);
        if (files.Length == 0 && query.Length == 0) throw new NotSupportedException("没有可复制的文件或原QueryPath。");
        var items = new List<NSPasteboardItem>();
        try
        {
            foreach (var path in files)
            {
                var item = new NSPasteboardItem(); items.Add(item);
                using var url = NSUrl.FromFilename(path);
                if (!item.SetStringForType(url.AbsoluteString!, FileUrlType)) throw new IOException("系统拒绝文件URL数据。");
            }
            // ArchivePolicy.None 仍复制原QueryPath，原项目也允许没有FileDrop的剪贴板。
            if (items.Count == 0) items.Add(new NSPasteboardItem());
            if (!items[0].SetStringForType(FileClipboardCodec.EncodeQueryPaths(query), FileClipboardCodec.QueryPathsType)) throw new IOException("系统拒绝原QueryPath数据。");
            if (content.Text is { } text && !items[0].SetStringForType(text, TextType)) throw new IOException("系统拒绝路径文本。");
            token.ThrowIfCancellationRequested();
            var board = NSPasteboard.GeneralPasteboard; board.ClearContents();
            if (!board.WriteObjects(items.Cast<INSPasteboardWriting>().ToArray())) throw new IOException("写入系统文件剪贴板失败。");
            return true;
        }
        finally { foreach (var item in items) item.Dispose(); }
    }, token);
    /// <summary>读取同一原生快照，QueryPath与file URL分别保留，不消费剪贴板或加载文件。</summary>
    /// <param name="token">排队/读取前取消。</param><returns>有限文件/图片/HTML/URL快照；普通文本不作为文件。</returns>
    public Task<FileClipboardContent> ReadAsync(CancellationToken token) => OnMainThreadAsync(() =>
    {
        var board = NSPasteboard.GeneralPasteboard; var revision = board.ChangeCount;
        var items = board.PasteboardItems ?? [];
        if (items.Length > FileClipboardCodec.MaximumItems) throw new NotSupportedException("剪贴板文件数量超过支持上限。");
        var urls = items.Where(item => item.Types.Contains(FileUrlType)).Select(item => item.GetStringForType(FileUrlType)).ToArray();
        var files = FileClipboardCodec.DecodeFileUrls(urls);
        var raw = board.GetStringForType(FileClipboardCodec.QueryPathsType);
        var query = raw is null ? [] : FileClipboardCodec.DecodeQueryPaths(raw);
        ContentDropData? content = null;
        if (query.Length == 0)
        {
            var web = items.Where(i => i.Types.Contains("public.url")).Select(i => i.GetStringForType("public.url"))
                .OfType<string>().Where(s => s.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 0 || web.Length > 0)
            {
                var html = board.GetStringForType("public.html");
                if (html?.Length > 1024 * 1024) throw new NotSupportedException("剪贴板HTML超过1MiB预算。");
                var images = new List<ContentDropImage>();
                foreach (var item in items)
                {
                    var type = new[] { "public.png", "public.tiff", "public.jpeg" }.FirstOrDefault(item.Types.Contains);
                    if (type is null) continue;
                    if (images.Count >= 16) throw new NotSupportedException("剪贴板图片超过16项预算。");
                    using var data = item.GetDataForType(type);
                    if (data is null) continue;
                    if (data.Length > ContentDropReceiver.MaximumFileBytes) throw new NotSupportedException("剪贴板图片超过64MiB预算。");
                    images.Add(new(data.ToArray(), IsBitmap: true));
                }
                content = new(images, html, web);
            }
        }
        if (revision != board.ChangeCount) throw new IOException("读取期间剪贴板已改变，请重试。");
        return new FileClipboardContent(files, query, Content: content);
    }, token);
    /// <summary>原生对象只在主线程使用，取消的排队调用不得晚到改写剪贴板。</summary>
    /// <param name="action">仅在主线程执行的完整原生读取或写入。</param><param name="token">排队等待及提交前取消。</param>
    /// <returns>真实原生结果，回调开始后不提前报告取消。</returns>
    private static async Task<T> OnMainThreadAsync<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 只取消排队等待。回调开始后由提交前检查决定，不能提前报告已提交写入被取消。
        int phase = 0;
        using var cancellation = token.Register(() =>
        { if (Interlocked.CompareExchange(ref phase, 2, 0) == 0) completion.TrySetCanceled(token); });
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) return;
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return await completion.Task.ConfigureAwait(false);
    }
}
