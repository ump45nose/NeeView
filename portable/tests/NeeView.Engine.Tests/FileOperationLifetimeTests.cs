using NeeView;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>关闭与原分类忙碌锁协调；未知落点必须保留源页和历史，不无限等待实体结果。</summary>
public sealed class FileOperationLifetimeTests
{
    private sealed class BlockedBackend : IFileOperationBackend, IInterruptibleFileOperations
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FileTransferResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Interrupted { get; private set; }
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => Task.FromResult(File.Exists(path));
        public Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token) { Started.TrySetResult(); return _result.Task; }
        public void InterruptPendingOperations() { Interrupted = true; _result.TrySetException(new IOException("结果未确认，恢复记录已保留。")); }
        public Task ReleaseAsync(FileTransferResult result) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => Task.CompletedTask;
    }
    [Fact]
    public async Task CloseInterruptsNativeFileRequestBeforeWaitingForNavigationGate()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token);
        await using var operation = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new BlockedBackend(); var moves = new DestinationMoveService(backend);
        operation.AttachFileOperations(moves, backend, images); Config.Current.System.IsFileWriteAccessEnabled = true;
        await operation.OpenAsync(f.Images, token);
        var current = operation.Book!.CurrentPage!; var source = current.ArchiveEntry.FilePath!;
        var classify = operation.ClassifyAsync(new DestinationFolder("目标", Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName), false, token);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        await operation.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token); await classify;
        Assert.True(backend.Interrupted); Assert.False(moves.IsBusy); Assert.Equal(0, moves.UndoCount);
        Assert.Contains("结果未确认", moves.Error); Assert.True(File.Exists(source)); Assert.Null(operation.Book);
        await state.LoadAsync(token); Assert.Equal(f.Images, state.GetLastBook()!.Path);
    }
}
