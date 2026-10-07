using System.Diagnostics;
using System.Text;
using NeeView;
using NeeView.Backends;
namespace NeeView.Backends.MacOS.Tests;

/// <summary>真实同exe无窗口文件请求、取消/原生停滞、提交后丢失响应及原日志恢复。</summary>
public sealed class FileWorkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "neeview-worker-" + Guid.NewGuid().ToString("N"));
    private string Recovery => Path.Combine(_root, "recovery");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public FileWorkerTests() => Directory.CreateDirectory(_root);
    private static ProcessStartInfo Start(string? fault = null)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        start.ArgumentList.Add(fault is null ? FileOperationWorkerHost.Argument : "--neeview-test-file-worker");
        if (fault is not null) start.ArgumentList.Add(fault);
        return start;
    }
    private FileOperationWorkerBackend Backend(string? fault = null, int idle = 1000) => new(Recovery, () => Start(fault), TimeSpan.FromMilliseconds(idle), TimeSpan.FromMilliseconds(250));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task RealMoveOverwriteUndoRenameAndRecoveryUseSameProtocol()
    {
        using var backend = Backend(idle: 5000);
        var source = Path.Combine(_root, "原图.png"); var destination = Path.Combine(_root, "目标.png");
        await File.WriteAllTextAsync(source, "original", Token); await File.WriteAllTextAsync(destination, "previous", Token);
        var moved = await backend.TransferAsync(new(source, destination, true, true), Token);
        Assert.False(File.Exists(source)); Assert.Equal("original", await File.ReadAllTextAsync(destination, Token));
        Assert.Equal("previous", await File.ReadAllTextAsync(moved.Backup!, Token));
        var undone = await backend.TransferAsync(new(destination, source, true, RestoreBackup: moved.Backup,
            ExpectedSourceHash: moved.ContentHash, ExpectedRestoreHash: moved.BackupHash), Token);
        Assert.Equal("original", await File.ReadAllTextAsync(source, Token)); Assert.Equal("previous", await File.ReadAllTextAsync(destination, Token));
        await backend.ReleaseAsync(undone); await backend.ReleaseAsync(moved);
        Assert.Empty(await backend.RecoverAsync(Token));
        var target = await backend.GetRenameTargetAsync(source, Token);
        var plan = await backend.PlanRenameAsync(target, "重命名.png", Token);
        await backend.RenameAsync(plan, Token); Assert.True(await backend.WasRenamedAsync(plan, Token));
        Assert.True(await backend.FileExistsAsync(plan.Destination, Token));
        await backend.CreateDirectoryAsync(_root, "目标目录", Token);
        var copyPlan = await backend.PlanBookTransferAsync(plan.Destination, Path.Combine(_root, "目标目录"), Token);
        var copied = await backend.TransferBookAsync(copyPlan, false, Token);
        Assert.True(File.Exists(plan.Destination)); Assert.Equal("original", await File.ReadAllTextAsync(copied.Destination, Token));
        await backend.ReleaseAsync(copied); Assert.Empty(await backend.RecoverAsync(Token));
    }
    [Fact]
    public async Task DirectoryAndNullableRecoveryResultSurviveSourceGeneratedProtocol()
    {
        using var backend = Backend(idle: 5000);
        var source = Directory.CreateDirectory(Path.Combine(_root, "目录")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "图.txt"), "pixels", Token); Directory.CreateDirectory(Path.Combine(source, "empty"));
        var plan = await backend.PlanPathTransferAsync(source, Path.Combine(_root, "复制目录"), Token);
        var copy = await backend.TransferBookAsync(plan, false, Token);
        Assert.True(Directory.Exists(Path.Combine(copy.Destination, "empty"))); await backend.ReleaseAsync(copy);
        var missing = new BookRenamePlan(plan.Target, Path.Combine(_root, "不存在"), false, false);
        // 两端均存在/不存在的模糊身份保持null，不误转成false。
        Directory.Move(source, Path.Combine(_root, "外部移走"));
        Assert.Null(await backend.WasRenamedAsync(missing, Token));
    }
    [Fact]
    public async Task NativeCallIgnoringCancelHasBoundedExitAndFutureRequestsStillWork()
    {
        using var backend = Backend("hang");
        var clock = Stopwatch.StartNew(); var active = backend.FileExistsAsync(_root, Token);
        await Task.Delay(150, Token); var queued = backend.FileExistsAsync(_root, Token);
        backend.InterruptPendingOperations();
        Assert.Contains("结果未确认", (await Assert.ThrowsAsync<IOException>(() => active)).Message);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        using var healthy = Backend(); Assert.True(await healthy.FileExistsAsync(_root, Token));
    }
    [Fact]
    public async Task IdleTimeoutDoesNotPretendRollbackAndKeepsExistingMaterials()
    {
        Directory.CreateDirectory(Recovery); var material = Path.Combine(Recovery, "keep.marker"); await File.WriteAllTextAsync(material, "preserve", Token);
        using var backend = Backend("hang", idle: 200); var clock = Stopwatch.StartNew();
        Assert.Contains("恢复记录已保留", (await Assert.ThrowsAsync<IOException>(() => backend.FileExistsAsync(_root, Token))).Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5)); Assert.Equal("preserve", await File.ReadAllTextAsync(material, Token));
    }
    [Fact]
    public async Task LongHealthyProgressDoesNotTriggerIdleTimeout()
    { using var backend = Backend("progress", idle: 1000); Assert.True(await backend.FileExistsAsync(_root, Token)); }
    [Fact]
    public async Task ConfirmedResultWinsOverLateCancellation()
    {
        using var backend = Backend("late-result", idle: 5000); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(150); Assert.True(await backend.FileExistsAsync(_root, cancellation.Token));
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("no-result")]
    public async Task InvalidProtocolReportsUnknownOutcome(string fault)
    { using var backend = Backend(fault); Assert.Contains("结果未确认", (await Assert.ThrowsAsync<IOException>(() => backend.FileExistsAsync(_root, Token))).Message); }
    [Fact]
    public async Task SuccessfulCommitWithLostResponseLeavesJournalForRealRecovery()
    {
        var source = Path.Combine(_root, "source.txt"); var target = Path.Combine(_root, "target.txt"); await File.WriteAllTextAsync(source, "original", Token);
        using (var broken = Backend("drop-result", idle: 500))
            await Assert.ThrowsAsync<IOException>(() => broken.TransferAsync(new(source, target, true), Token));
        Assert.False(File.Exists(source)); Assert.Equal("original", await File.ReadAllTextAsync(target, Token));
        Assert.NotEmpty(Directory.GetFiles(Recovery, "*.json"));
        using var backend = Backend(idle: 5000); Assert.Empty(await backend.RecoverAsync(Token));
        Assert.Empty(Directory.GetFiles(Recovery, "*.json")); Assert.Equal("original", await File.ReadAllTextAsync(target, Token));
    }
}

/// <summary>仅测试宿主提供的故障模式，不进入正式产品；不创建AppKit窗口。</summary>
internal static class FileWorkerFaultHost
{
    public static async Task<int> RunAsync(string mode)
    {
        if (mode == "drop-result")
        {
            await FileOperationWorkerHost.RunAsync(new StreamReader(Console.OpenStandardInput()), new DropResultWriter());
            await Task.Delay(Timeout.Infinite); return 0;
        }
        _ = await Console.In.ReadLineAsync();
        switch (mode)
        {
            case "hang": await Task.Delay(Timeout.Infinite); break;
            case "progress":
                for (int i = 0; i < 30; i++) { Console.WriteLine("{\"Progress\":true}"); Console.Out.Flush(); await Task.Delay(75); }
                Console.WriteLine("{\"Completed\":true,\"Flag\":true}"); break;
            case "late-result":
                _ = await Console.In.ReadLineAsync(); Console.WriteLine("{\"Completed\":true,\"Flag\":true}"); break;
            case "malformed": Console.WriteLine("not-json"); break;
            case "oversized": Console.WriteLine(new string('x', 300000)); break;
            case "no-result": break;
            default: return 2;
        }
        Console.Out.Flush(); return 0;
    }
    private sealed class DropResultWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        { if (value?.Contains("\"Completed\":true", StringComparison.Ordinal) != true) Console.WriteLine(value); }
        public override void Flush() => Console.Out.Flush();
    }
}
