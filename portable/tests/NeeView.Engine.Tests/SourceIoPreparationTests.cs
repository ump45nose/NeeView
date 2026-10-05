using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>模拟不可即时中断的只读系统调用，验证期限、晚到清理和写入授权边界。</summary>
public sealed class SourceIoPreparationTests
{
    private sealed class LateLease : IDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Disposed.TrySetResult();
    }
    [Fact]
    public async Task RealProgressAllowsLongReadsButCannotHideAStall()
    {
        var progress = new SourceIo.ReadProgress();
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(8, await SourceIo.RunReadAsync(async t =>
        {
            for (int i = 0; i < 8; i++) { await Task.Delay(20, t); progress.Mark(); }
            return 8;
        }, token, TimeSpan.FromMilliseconds(80), progress));
        await Assert.ThrowsAsync<TimeoutException>(() => SourceIo.RunReadAsync(async t =>
        { progress.Mark(); await Task.Delay(Timeout.Infinite, t); return 0; }, token, TimeSpan.FromMilliseconds(80), progress));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredReadsKeepBothSlotsAndCannotAuthorizeFileWrites(bool cancel)
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leases = new[] { new LateLease(), new LateLease() };
        var started = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var readTokens = new CancellationToken[2];
        try
        {
            for (int index = 0; index < 2; index++)
            {
                int i = index; using var cancellation = new CancellationTokenSource();
                var read = SourceIo.RunReadAsync(async token =>
                {
                    readTokens[i] = token; started[i].TrySetResult();
                    await release.Task; // native模拟故意忽略取消，槽仍必须被保留。
                    return leases[i];
                }, cancellation.Token, cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100));
                await started[i].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                if (cancel) cancellation.Cancel();
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
                else await Assert.ThrowsAsync<TimeoutException>(() => read);
                Assert.True(readTokens[i].IsCancellationRequested);
            }
            bool entered = false;
            await Assert.ThrowsAsync<TimeoutException>(() => SourceIo.RunReadAsync(_ => { entered = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(50)));
            Assert.False(entered); // 两个晚到任务未返回，不能无限启动新系统调用。

            var backend = new FileOperationBackend(Path.Combine(fixture.State, "Recovery"));
            var folder = Directory.CreateDirectory(Path.Combine(fixture.Root, "target")).FullName;
            var source = Path.Combine(fixture.Images, "001.png"); var destination = Path.Combine(folder, "001.png");
            var bytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.TransferAsync(new(source, destination, true), stop.Token));
            Assert.False(File.Exists(destination)); Assert.False(Directory.Exists(Path.Combine(fixture.State, "Recovery")));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        }
        finally { release.TrySetResult(); }
        foreach (var lease in leases) await lease.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(42, await SourceIo.RunReadAsync(_ => Task.FromResult(42), TestContext.Current.CancellationToken, TimeSpan.FromSeconds(5)));
    }
}
