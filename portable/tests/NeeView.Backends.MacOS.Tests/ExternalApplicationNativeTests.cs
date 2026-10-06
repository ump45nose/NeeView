using System.Text.Json;
using NeeView.Backends;
namespace NeeView.Backends.MacOS.Tests;

/// <summary>真实后台进程参数与工作目录，不激活浏览器/用户应用或剪贴板。</summary>
public sealed class ExternalApplicationNativeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteralArgumentsAndWorkingDirectoryReachExecutableOrApp(bool appBundle)
    {
        var root = Path.Combine(Path.GetTempPath(), "NeeView-P5-External-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "probe"); var command = executable;
            if (appBundle)
            {
                command = Path.Combine(root, "Probe.app"); var contents = Path.Combine(command, "Contents"); Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
                executable = Path.Combine(contents, "MacOS", "probe");
                await File.WriteAllTextAsync(Path.Combine(contents, "Info.plist"), "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleExecutable</key><string>probe</string><key>CFBundleIdentifier</key><string>net.neeview.external-probe</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>", Token);
            }
            // shell只在合成的外部应用内部逐字打印已接收参数；产品没有shell调用。
            // 完整写入后原子发布；文件存在必须代表探针已写完，避免读到刚创建的空文件。
            await File.WriteAllTextAsync(executable, "#!/bin/sh\noutput=\"$1\"\nshift\nprintf '%s\\n' \"$PWD\" \"$@\" > \"$output.tmp\"\nmv \"$output.tmp\" \"$output\"\n", Token);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var output = Path.Combine(root, "args.txt"); var sentinel = Path.Combine(root, "must-not-exist");
            string[] args = [output, "two words", "'\"quoted", "$(touch " + sentinel + ")", "`touch " + sentinel + "`", "{File}", ""];
            await new MacPlatformService().OpenExternalApplicationAsync(new(command, args, root), Token);
            for (int i = 0; i < 200 && !File.Exists(output); i++) await Task.Delay(10, Token);
            Assert.True(File.Exists(output), "后台应用未在等待期限内完成探针输出");
            var actual = await File.ReadAllLinesAsync(output, Token);
            // macOS 的 /var 是 /private/var 别名，shell 的 PWD 会报告系统解析后的同一目录。
            Assert.Equal(await new ArchiveFactory().GetPhysicalPathAsync(root, Token), actual[0]);
            Assert.Equal(args.Skip(1), actual.Skip(1)); Assert.False(File.Exists(sentinel));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task CanceledRequestNeverStartsProcess()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MacPlatformService().OpenExternalApplicationAsync(new("/usr/bin/true", [], null), canceled.Token));
    }
    [Fact]
    public async Task InvalidAssociationAndWorkingDirectoryFailBeforeSystemSubmission()
    {
        var platform = new MacPlatformService();
        await Assert.ThrowsAsync<ArgumentException>(() => platform.OpenExternalApplicationAsync(new(null, ["first", "second"], null), Token));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => platform.OpenExternalApplicationAsync(new("/usr/bin/true", [], "/absent-" + Guid.NewGuid().ToString("N")), Token));
        await Assert.ThrowsAsync<FileNotFoundException>(() => platform.OpenExternalApplicationAsync(new(null, ["/absent-" + Guid.NewGuid().ToString("N")], null), Token));
    }
}
