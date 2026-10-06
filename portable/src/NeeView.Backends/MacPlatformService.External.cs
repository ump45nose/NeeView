using AppKit;
using Foundation;
using NeeView;
namespace NeeView.Backends;

public sealed partial class MacPlatformService
{
    /// <summary>默认系统关联或显式可执行文件/.app；参数按字面传递，不经过shell。</summary>
    /// <param name="request">Engine捕获并展开占位符的参数数组。</param><param name="token">仅系统提交前可取消，启动后回报实际成功。</param>
    public Task OpenExternalApplicationAsync(ExternalAppLaunchRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) && !Directory.Exists(request.WorkingDirectory))
            throw new DirectoryNotFoundException("外部应用工作目录不存在。" + request.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            if (request.Arguments.Count != 1) throw new ArgumentException("系统默认关联必须且只能接收一个路径或 URL。", nameof(request));
            // 默认关联由LaunchServices选择应用，没有可设置工作目录的系统入口；不能静默忽略。
            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) throw new NotSupportedException("需要工作目录时请配置明确的应用命令。");
            var value = request.Arguments[0]; NSUrl url;
            if (Path.IsPathFullyQualified(value))
            {
                if (!File.Exists(value) && !Directory.Exists(value)) throw new FileNotFoundException("外部打开目标不存在。", value);
                url = NSUrl.FromFilename(value);
            }
            else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile && !string.IsNullOrEmpty(uri.Host) && !uri.IsLoopback) throw new NotSupportedException("远程file URL不能作为本机文件打开。");
                if (uri.IsFile && !File.Exists(uri.LocalPath) && !Directory.Exists(uri.LocalPath)) throw new FileNotFoundException("外部打开目标不存在。", uri.LocalPath);
                url = new NSUrl(uri.AbsoluteUri);
            }
            else throw new ArgumentException("系统默认关联需要完整路径或绝对 URL。", nameof(request));
            using (url)
            {
                Exception? failure = null;
                NSApplication.SharedApplication.InvokeOnMainThread(() =>
                {
                    try { token.ThrowIfCancellationRequested(); if (!NSWorkspace.SharedWorkspace.OpenUrl(url)) throw new IOException("系统无法打开外部目标。"); }
                    catch (Exception ex) { failure = ex; }
                });
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return Task.CompletedTask;
        }
        var executable = request.Command;
        if (executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            using var bundle = NSBundle.FromPath(executable) ?? throw new FileNotFoundException("外部应用包不存在。", executable);
            executable = bundle.ExecutablePath ?? throw new InvalidOperationException("外部应用包没有可执行文件。");
        }
        var start = new System.Diagnostics.ProcessStartInfo { FileName = executable, UseShellExecute = false };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) start.WorkingDirectory = request.WorkingDirectory;
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var process = new System.Diagnostics.Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("外部应用启动失败。");
        return Task.CompletedTask;
    }
}
