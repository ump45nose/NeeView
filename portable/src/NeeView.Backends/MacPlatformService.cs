using AppKit;
using Foundation;
using NeeView;

namespace NeeView.Backends;

/// <summary>正式 macOS 文件能力，使用 AppKit/Foundation 的真实系统结果。</summary>
public sealed class MacPlatformService : IPlatformService
{
    private readonly SemaphoreSlim _icons = new(2);
    private readonly SemaphoreSlim _trash = new(1);
    private readonly Dictionary<string, byte[]?> _iconCache = new(StringComparer.Ordinal);
    /// <summary>使用NSWorkspace真实图标，原生对象在主线程创建/释放，PNG字节跨平台边界传递。</summary>
    public async Task<byte[]?> ReadFileIconAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':')) return null;
        await _icons.WaitAsync(token);
        try
        {
            lock (_iconCache) if (_iconCache.TryGetValue(path, out var cached)) return cached;
            var result = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) { result.TrySetCanceled(token); return; }
                    using var source = NSWorkspace.SharedWorkspace.IconForFile(path);
                    using var image = (NSImage)source.Copy();
                    image.Size = new CoreGraphics.CGSize(32, 32);
                    using var tiff = image.AsTiff();
                    using var bitmap = new NSBitmapImageRep(tiff!);
                    using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
                    var data = png?.ToArray(); if (data?.Length > 256 * 1024) data = null;
                    lock (_iconCache) { if (_iconCache.Count >= 64) _iconCache.Remove(_iconCache.Keys.First()); _iconCache[path] = data; }
                    result.TrySetResult(data);
                }
                catch (Exception ex) { result.TrySetException(ex); }
            });
            return await result.Task;
        }
        finally { _icons.Release(); }
    }
    /// <summary>系统 Finder 定位输入文件；不通过 shell 拼接路径。</summary>
    public Task RevealAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        NSApplication.SharedApplication.InvokeOnMainThread(() => { using var url = NSUrl.FromFilename(path); NSWorkspace.SharedWorkspace.ActivateFileViewer([url]); });
        return Task.CompletedTask;
    }
    /// <summary>系统废纸篓操作，失败传播 NSError，禁止永久删除回退。</summary>
    public async Task TrashAsync(string path, CancellationToken token = default)
    {
        await _trash.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0) throw new FileNotFoundException("源图片已不存在或不是普通文件。", path);
                if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new NotSupportedException("链接的删除尚未迁移。");
                using var url = NSUrl.FromFilename(file.FullName);
                bool success = NSFileManager.DefaultManager.TrashItem(url, out var resulting, out var error);
                try { if (!success) throw new IOException(error?.LocalizedDescription ?? "移至废纸篓失败。"); }
                finally { resulting?.Dispose(); error?.Dispose(); }
                // 系统已经提交时不再抛晚取消，调用方必须更新原索引。
            }, token);
        }
        finally { _trash.Release(); }
    }
}
