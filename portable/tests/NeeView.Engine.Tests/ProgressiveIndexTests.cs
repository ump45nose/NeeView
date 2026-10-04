using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>受控后续批次验证首批可阅读、Page身份、排序/取消及真实目录先图；不是设备帧率测试。</summary>
public sealed class ProgressiveIndexTests
{
    [Fact]
    public async Task FirstBatchIsReadableBeforeEnumerationEndsAndSortingKeepsPage()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = new BatchArchive(fixture.Images); var sources = new BatchFactory(source);
        await using var operation = new BookOperation(sources, new Decoder(), state);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Changed += (_, _) => { if (operation.Book?.IsIndexing == true && !operation.IsLoading) ready.TrySetResult(); };
        var opening = operation.OpenAsync(source.Path, TestContext.Current.CancellationToken);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(opening.IsCompleted); Assert.Equal(2, operation.Book!.Pages.Count);
        await operation.JumpAsync(1); var page = operation.Book.CurrentPage;
        await operation.MoveAsync(1); Assert.Same(page, operation.Book.CurrentPage); Assert.Null(operation.Error); // 尚未到真实页尾。
        await operation.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending);
        source.Continue.TrySetResult(); await opening;
        Assert.Same(page, operation.Book.CurrentPage); Assert.False(operation.Book.IsIndexing); Assert.Null(operation.Book.IndexError);
        Assert.Equal(new[] { "004.png", "003.png", "002.png", "001.png" }, operation.Book.Pages.Select(p => p.EntryName));
        Assert.Equal(page!.Index, operation.Position.Index);
    }

    [Fact]
    public async Task ExplicitLateEntryNeverCommitsWrongInitialPage()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = new BatchArchive(fixture.Images) { RequestedEntryName = "001.png" };
        await using var operation = new BookOperation(new BatchFactory(source), new Decoder(), state);
        var opening = operation.OpenAsync(source.Path, TestContext.Current.CancellationToken); await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(operation.Book); Assert.True(operation.IsLoading);
        source.Continue.TrySetResult(); await opening;
        Assert.Equal("001.png", operation.Book!.CurrentPage!.EntryName); Assert.Null(operation.Error);
    }

    [Fact]
    public async Task FailureAfterCommitPreservesReadablePagesAndReportsIncompleteIndex()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = new BatchArchive(fixture.Images) { FailAfterFirst = true };
        await using var operation = new BookOperation(new BatchFactory(source), new Decoder(), state);
        var opening = operation.OpenAsync(source.Path, TestContext.Current.CancellationToken); await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        source.Continue.TrySetResult(); await opening;
        Assert.Equal(2, operation.Book!.Pages.Count); Assert.False(operation.Book.IsIndexing); Assert.NotNull(operation.Book.IndexError);
        Assert.Contains("索引未完成", operation.Error!); await operation.JumpAsync(1); Assert.Equal(1, operation.Position.Index);
    }

    [Fact]
    public async Task NewOpenCancelsOldIndexAndDoesNotAppendToNewBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = new BatchArchive(fixture.Images); var next = new BatchArchive(fixture.Root + "/next"); next.Continue.SetResult();
        await using var operation = new BookOperation(new BatchFactory(source, next), new Decoder(), state);
        var oldOpen = operation.OpenAsync(source.Path, TestContext.Current.CancellationToken); await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var oldBook = operation.Book; await operation.OpenAsync(next.Path, TestContext.Current.CancellationToken); await oldOpen;
        Assert.NotSame(oldBook, operation.Book); Assert.Equal(4, operation.Book!.Pages.Count); Assert.True(source.IsDisposed);
        Assert.Equal(2, oldBook!.Pages.Count); Assert.False(operation.Book.IsIndexing);
    }

    [Fact]
    public async Task ActualFolderBatchAndExplicitImageDoNotWaitForFullSnapshot()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 300; i++) File.WriteAllBytes(Path.Combine(fixture.Images, $"extra-{i}.png"), []);
        await using var source = new FolderArchive(fixture.Images) { RequestedEntryName = "003.png" };
        await using var entries = source.EnumerateEntryBatchesAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await entries.MoveNextAsync()); Assert.Single(entries.Current); Assert.Equal("003.png", entries.Current[0].EntryName);
        var result = entries.Current.ToList();
        while (await entries.MoveNextAsync()) { Assert.InRange(entries.Current.Count, 1, 128); result.AddRange(entries.Current); }
        Assert.Equal(305, result.Count); Assert.Equal(305, result.Select(e => e.Id).Distinct().Count());
        Assert.Single(result, e => e.EntryName == "003.png");
    }

    private sealed class BatchFactory(params BatchArchive[] sources) : IArchiveFactory
    {
        public Task<Archive> OpenAsync(string path, CancellationToken token) => Task.FromResult<Archive>(sources.Single(s => s.Path == path));
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<FolderItem>>([]);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<FolderItem>>([]);
    }
    private sealed class BatchArchive(string path) : Archive(path)
    {
        public override bool IsDirectory => true;
        public bool FailAfterFirst { get; init; }
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async IAsyncEnumerable<IReadOnlyList<ArchiveEntry>> EnumerateEntryBatchesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            yield return new[] { Entry(0, "003.png"), Entry(1, "004.png") }; Waiting.TrySetResult();
            await Continue.Task.WaitAsync(token); token.ThrowIfCancellationRequested();
            if (FailAfterFirst) throw new IOException("fixture NAS disconnected");
            yield return new[] { Entry(2, "001.png"), Entry(3, "002.png") };
        }
        private ArchiveEntry Entry(int id, string name) => new(this) { Id = id, RawEntryName = name };
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => throw new NotSupportedException("Test requires streaming");
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => Task.FromResult<Stream>(new MemoryStream());
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Decoder : IImageDecoder
    {
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => Task.FromResult(new ImageInfo(new(400, 600), "fixture"));
        public Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
