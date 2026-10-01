using System.Security.Cryptography;
using NeeView.Application;
using NeeView.Core;
using Xunit;

#pragma warning disable xUnit1051
namespace NeeView.Portable.Tests;

public sealed class RecoveryTests
{
    /// <summary>最终日志清理失败时文件与身份均保持提交后的状态，重试恢复只清日志。</summary>
    [Fact]
    public async Task FinalizeFailureDoesNotReverseCommittedIdentity()
    {
        await using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "a.jpg"); await File.WriteAllTextAsync(path, "picture");
        var target = Path.Combine(workspace.Root, "target"); Directory.CreateDirectory(target);
        var page = await workspace.PageAsync(path); var fault = new RemoveFaultStore(workspace.States);
        var service = new FileActionService(workspace.States, fault, new TestPlatform(), Path.Combine(workspace.Root, "backups"));
        Assert.False((await service.ExecuteAsync(page, FileActionKind.Move, target)).Success);
        var moved = Path.Combine(target, "a.jpg"); Assert.False(File.Exists(path)); Assert.True(File.Exists(moved));
        Assert.Equal(page.Id, await workspace.States.GetContentAsync(new(moved), default));
        Assert.Equal("finalized", Assert.Single(await workspace.States.RecoveriesAsync()).Stage);
        Assert.True(Assert.Single(await service.RecoverAsync()).Resolved);
        Assert.Equal(page.Id, await workspace.States.GetContentAsync(new(moved), default));
        Assert.Empty(await workspace.States.RecoveriesAsync());
    }
    /// <summary>模拟目标已提交但源尚未删除的中断，启动完成移动并保持源内容身份。</summary>
    [Fact]
    public async Task RestartFinishesCommittedMoveAndRestoresCoveredFile()
    {
        await using var workspace = new TestWorkspace();
        var source = Path.Combine(workspace.Root, "source.jpg"); var target = Path.Combine(workspace.Root, "target.jpg");
        var restore = Path.Combine(workspace.Root, "backup");
        await File.WriteAllTextAsync(source, "picture"); await File.WriteAllTextAsync(target, "picture"); await File.WriteAllTextAsync(restore, "displaced");
        var page = await workspace.PageAsync(source);
        await workspace.States.RecordOperationAsync(new("restart", source, target, null, null, "target-committed", SourceDigest: Digest("picture"), Content: page.Id,
            RestoreBackup: restore, RestorePath: source, RestoreDigest: Digest("displaced")));
        Assert.True(Assert.Single(await workspace.FileService().RecoverAsync()).Resolved);
        Assert.Equal("displaced", await File.ReadAllTextAsync(source)); Assert.Equal("picture", await File.ReadAllTextAsync(target));
        Assert.Equal(page.Id, await workspace.States.GetContentAsync(new(target), default));
    }
    /// <summary>源在中断后被替换时拒绝删除，留下恢复日志和两侧文件。</summary>
    [Fact]
    public async Task RecoveryNeverDeletesExternallyReplacedSource()
    {
        await using var workspace = new TestWorkspace();
        var source = Path.Combine(workspace.Root, "source.jpg"); var target = Path.Combine(workspace.Root, "target.jpg");
        await File.WriteAllTextAsync(source, "external"); await File.WriteAllTextAsync(target, "picture");
        await workspace.States.RecordOperationAsync(new("changed", source, target, null, null, "target-committed", SourceDigest: Digest("picture")));
        Assert.False(Assert.Single(await workspace.FileService().RecoverAsync()).Resolved);
        Assert.Equal("external", await File.ReadAllTextAsync(source)); Assert.Single(await workspace.States.RecoveriesAsync());
    }
    /// <summary>文件描述版本过期或历史图片被替换时拒绝分类、撤销，保留历史栈。</summary>
    [Fact]
    public async Task StaleDescriptorAndChangedUndoTargetAreRejected()
    {
        await using var workspace = new TestWorkspace();
        var source = Path.Combine(workspace.Root, "a.jpg"); var folder = Path.Combine(workspace.Root, "dest"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(source, "old"); var stale = await workspace.PageAsync(source); await File.WriteAllTextAsync(source, "different size");
        var service = workspace.FileService(); Assert.False((await service.ExecuteAsync(stale, FileActionKind.Move, folder)).Success);
        var fresh = await workspace.PageAsync(source); Assert.True((await service.ExecuteAsync(fresh, FileActionKind.Move, folder)).Success);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.jpg"), "external");
        Assert.False((await service.UndoAsync()).Success); Assert.True(service.CanUndo);
    }
    /// <summary>目录图片移动后目标书籍恢复原锚点，书签定位随内容身份迁移。</summary>
    [Fact]
    public async Task CrossDirectoryMoveUpdatesReadingStateAndBookmarkBook()
    {
        await using var workspace = new TestWorkspace();
        var from = Path.Combine(workspace.Root, "from"); var to = Path.Combine(workspace.Root, "to"); Directory.CreateDirectory(from); Directory.CreateDirectory(to);
        var path = Path.Combine(from, "a.jpg"); await File.WriteAllTextAsync(path, "picture"); var page = await workspace.PageAsync(path);
        var book = await workspace.States.GetBookAsync(new(from), default);
        await workspace.States.SaveAsync(new(book, new(from), new(page.Id), new() { DoublePage = true }, DateTimeOffset.UtcNow));
        await workspace.States.SaveBookmarkAsync(new("bookmark", null, 0, "图片", book, new(from), new(page.Id)));
        Assert.True((await workspace.FileService().ExecuteAsync(page, FileActionKind.Move, to)).Success);
        var targetBook = await workspace.States.GetBookAsync(new(to), default); var state = await workspace.States.GetAsync(targetBook);
        Assert.Equal(page.Id, state!.Anchor!.Content); Assert.True(state.Options.DoublePage);
        var bookmark = Assert.Single(await workspace.States.BookmarksAsync()); Assert.Equal(targetBook, bookmark.Book); Assert.Equal(to, bookmark.Locator!.Path);
    }
    /// <summary>输入夹具文本，返回与实际文件一致的摘要。</summary>
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    /// <summary>只注入一次删除日志故障，验证提交后处理，其他状态工作保持真实 SQLite。</summary>
    private sealed class RemoveFaultStore(IReaderStateStore inner) : IReaderStateStore
    {
        private bool _fail = true;
        public Task RemoveOperationAsync(string id, CancellationToken token = default)
        { if (_fail) { _fail = false; throw new IOException("模拟日志清理失败"); } return inner.RemoveOperationAsync(id, token); }
        public Task<ReadingState?> GetAsync(BookId book, CancellationToken token = default) => inner.GetAsync(book, token);
        public Task SaveAsync(ReadingState state, CancellationToken token = default) => inner.SaveAsync(state, token);
        public Task<IReadOnlyList<ReadingState>> HistoryAsync(CancellationToken token = default) => inner.HistoryAsync(token);
        public Task<IReadOnlyList<Bookmark>> BookmarksAsync(CancellationToken token = default) => inner.BookmarksAsync(token);
        public Task SaveBookmarkAsync(Bookmark bookmark, CancellationToken token = default) => inner.SaveBookmarkAsync(bookmark, token);
        public Task DeleteBookmarkAsync(string id, CancellationToken token = default) => inner.DeleteBookmarkAsync(id, token);
        public Task RecordOperationAsync(RecoveryOperation operation, CancellationToken token = default) => inner.RecordOperationAsync(operation, token);
        public Task<IReadOnlyList<RecoveryOperation>> RecoveriesAsync(CancellationToken token = default) => inner.RecoveriesAsync(token);
    }
}
