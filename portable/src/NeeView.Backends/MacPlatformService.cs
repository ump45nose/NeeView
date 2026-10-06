using AppKit;
using Foundation;
using NeeView;

namespace NeeView.Backends;

/// <summary>正式 macOS 文件能力，使用 AppKit/Foundation 的真实系统结果。</summary>
public sealed class MacPlatformService : IPlatformService
{
    /// <summary>版本窗口链接替换原ExternalProcess；只允许网页和本机文件。</summary>
    /// <param name="uri">绝对http/https或本机file URI。</param><param name="token">系统提交前取消，提交后返回实际结果。</param>
    public Task OpenUriAsync(Uri uri, CancellationToken token = default)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http" or "file") || uri.IsFile && uri.Host.Length > 0 && !uri.IsLoopback)
            throw new NotSupportedException("只支持网页或本机文件链接。");
        token.ThrowIfCancellationRequested();
        if (uri.IsFile && !File.Exists(uri.LocalPath)) throw new FileNotFoundException("许可文件未随应用安装。", uri.LocalPath);
        NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            token.ThrowIfCancellationRequested(); using var url = new NSUrl(uri.AbsoluteUri);
            if (!NSWorkspace.SharedWorkspace.OpenUrl(url)) throw new IOException("系统无法打开链接。");
        });
        return Task.CompletedTask;
    }
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
    /// <summary>原打开主题目录的系统替换点；Finder显示目录内容，检查NSWorkspace真实返回值。</summary>
    /// <param name="path">Engine已准备的绝对目录。</param><param name="token">进入系统打开前取消；提交后不伪装为取消。</param>
    public Task OpenFolderAsync(string path, CancellationToken token = default)
    {
        NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            token.ThrowIfCancellationRequested();
            using var url = NSUrl.FromFilename(path);
            if (!NSWorkspace.SharedWorkspace.OpenUrl(url)) throw new IOException("Finder 无法打开自定义主题目录。");
        });
        return Task.CompletedTask;
    }
    /// <summary>系统废纸篓操作，失败传播 NSError，禁止永久删除回退。</summary>
    /// <param name="path">真实文件、目录或链接本身；不解析末段链接，父链接仍拒绝。</param>
    /// <param name="token">排队与进入系统调用前取消；提交后等待真实结果。</param>
    /// <returns>单槽系统操作完成任务，调用方依据实际成功协调索引/阅读状态。</returns>
    public async Task TrashAsync(string path, CancellationToken token = default)
    {
        await _trash.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                path = Path.GetFullPath(path);
                var attributes = File.GetAttributes(path);
                FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
                for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent is not null; parent = parent.Parent)
                    if (parent.LinkTarget is not null && !(parent.FullName == "/var" && parent.ResolveLinkTarget(true)?.FullName == "/private/var"))
                        throw new NotSupportedException("删除路径包含链接目录，尚未迁移。");
                token.ThrowIfCancellationRequested();
                using var url = NSUrl.FromFilename(entry.FullName);
                bool success = NSFileManager.DefaultManager.TrashItem(url, out var resulting, out var error);
                try { if (!success) throw new IOException(error?.LocalizedDescription ?? "移至废纸篓失败。"); }
                finally { resulting?.Dispose(); error?.Dispose(); }
                // 系统已经提交时不再抛晚取消，调用方必须更新原索引。
            // 取消由委托中的两次检查裁决；进入TrashItem后等待真实结果，不取消其完成任务。
            }, CancellationToken.None);
        }
        finally { _trash.Release(); }
    }
}
