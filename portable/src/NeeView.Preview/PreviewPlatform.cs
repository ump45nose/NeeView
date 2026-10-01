using System.Diagnostics;
using NeeView.Application;

namespace NeeView.Preview;

/// <summary>无 Xcode 的开发适配；正式分发使用 Platform.MacOS 系统绑定。</summary>
public sealed class PreviewPlatform : IPlatformService
{
    /// <summary>使用 macOS 的 open 命令定位 Finder 文件；参数以独立 argv 传递。</summary>
    public async Task RevealAsync(string path, CancellationToken token = default)
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false }; info.ArgumentList.Add("-R"); info.ArgumentList.Add(path);
        using var process = Process.Start(info)!; await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new IOException("Finder 定位失败。");
    }
    /// <summary>通过 Finder 系统能力移至废纸篓，不使用永久删除作为替代。</summary>
    public async Task TrashAsync(string path, CancellationToken token = default)
    {
        const string script = "on run argv\ntell application \"Finder\" to delete POSIX file (item 1 of argv)\nend run";
        var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardError = true };
        info.ArgumentList.Add("-e"); info.ArgumentList.Add(script); info.ArgumentList.Add(path);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(token); await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new IOException(await error);
    }
}
