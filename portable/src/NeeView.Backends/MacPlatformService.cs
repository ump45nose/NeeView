using AppKit;
using Foundation;
using NeeView;

namespace NeeView.Backends;

/// <summary>正式 macOS 文件能力，使用 AppKit/Foundation 的真实系统结果。</summary>
public sealed class MacPlatformService : IPlatformService
{
    /// <summary>系统 Finder 定位输入文件；不通过 shell 拼接路径。</summary>
    public Task RevealAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        NSApplication.SharedApplication.InvokeOnMainThread(() => { using var url = NSUrl.FromFilename(path); NSWorkspace.SharedWorkspace.ActivateFileViewer([url]); });
        return Task.CompletedTask;
    }
    /// <summary>系统废纸篓操作，失败传播 NSError，禁止永久删除回退。</summary>
    public Task TrashAsync(string path, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); using var url = NSUrl.FromFilename(path);
        if (!NSFileManager.DefaultManager.TrashItem(url, out var resulting, out var error))
        { var message = error?.LocalizedDescription ?? "移至废纸篓失败。"; error?.Dispose(); throw new IOException(message); }
        resulting?.Dispose(); error?.Dispose();
    }, token);
}
