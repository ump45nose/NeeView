using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>真实嵌套压缩、原模式/父来源/定位关系及资源生命周期，全部使用隔离夹具。</summary>
public sealed class NestedArchiveTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Outer(Fixture f, params (string File, string Name)[] files)
    {
        var path = Path.Combine(f.Root, Guid.NewGuid().ToString("N") + ".cbz");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files) archive.CreateEntryFromFile(file.File, file.Name);
        return path;
    }
    [Theory]
    [InlineData(ArchiveEntryCollectionMode.CurrentDirectory, 2)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubDirectories, 2)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubArchives, 6)]
    public async Task OriginalCollectionModesOnlyFlattenNestedArchiveWhenRequested(ArchiveEntryCollectionMode mode, int count)
    {
        using var f = new Fixture(); var path = Outer(f, (f.Zip, "inside.cbz"), (Path.Combine(f.Images, "001.png"), "outside.png"));
        var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.ArchiveRecursiveMode = mode;
        await using var operation = f.Operation(state); await operation.OpenAsync(path, Token);
        Assert.Null(operation.Error); Assert.Equal(count, operation.Book!.Pages.Count);
        if (mode == ArchiveEntryCollectionMode.IncludeSubArchives)
        {
            var page = operation.Book.Pages.Single(p => p.EntryName == "inside.cbz/003.png");
            Assert.Equal(Path.Combine(path, "inside.cbz", "003.png"), page.EntryFullName); Assert.Equal(path, page.ArchiveEntry.Archive.RootArchivePath);
            Assert.NotNull(page.ArchiveEntry.Archive.Parent); Assert.Equal(1, page.ArchiveEntry.Archive.NestingDepth);
            await using var stream = await page.ArchiveEntry.Archive.OpenEntryAsync(page.ArchiveEntry, Token);
            Assert.Equal(400, (await new MagickImageDecoder().ProbeAsync(stream, Token)).Size.Width);
        }
        else Assert.Contains(operation.Book.Pages, p => p.EntryName == "inside.cbz" && p.PageType.IsFolder());
    }
    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar.solid.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("7Zip.LZMA.7z")]
    [InlineData("7Zip.solid.7z")]
    public async Task NestedRarAndSevenZipKeepPhysicalReaderAndBorrowedParent(string fixture)
    {
        using var f = new Fixture(); var inner = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var path = Outer(f, (inner, "目录/内部" + Path.GetExtension(inner))); var factory = new ArchiveFactory();
        var baseline = NestedArchiveFile.ReservedBytes; await using var parent = await factory.OpenAsync(path, Token);
        var entry = Assert.Single(await parent.GetEntriesAsync(Token)); var child = await factory.OpenAsync(entry, Token);
        try
        {
            Assert.Same(parent, child.Parent); Assert.Same(entry, child.Source); Assert.Equal(entry.SystemPath, child.Path); Assert.Equal(path, child.RootArchivePath);
            Assert.True(NestedArchiveFile.ReservedBytes > baseline);
            var pages = (await child.GetEntriesAsync(Token)).Where(e => e.IsImage()).ToArray(); Assert.NotEmpty(pages);
            foreach (var page in pages.Take(2))
            { await using var stream = await child.OpenEntryAsync(page, Token); Assert.True((await new MagickImageDecoder().ProbeAsync(stream, Token)).Size.Width > 0); }
        }
        finally { await child.DisposeAsync(); }
        Assert.False(parent.IsDisposed); Assert.Single(await parent.GetEntriesAsync(Token)); Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
    }
    [Fact]
    public async Task NestedNamesAndDuplicateIdsUseOriginalSourceEntryInsteadOfPathReopen()
    {
        using var f = new Fixture(); var second = Outer(f, (Path.Combine(f.Images, "001.png"), "other.png"));
        var outer = Outer(f, (f.Zip, "重复.cbz"), (second, "重复.cbz")); var factory = new ArchiveFactory();
        await using var parent = await factory.OpenAsync(outer, Token); var entries = await parent.GetEntriesAsync(Token);
        Assert.Equal(2, entries.Count); Assert.NotEqual(entries[0].Id, entries[1].Id);
        await using var a = await factory.OpenAsync(entries[0], Token); await using var b = await factory.OpenAsync(entries[1], Token);
        Assert.Equal(5, (await a.GetEntriesAsync(Token)).Count); Assert.Equal("other.png", Assert.Single(await b.GetEntriesAsync(Token)).EntryName);
        Assert.Equal(entries[0].SystemPath, a.Path); Assert.Equal(entries[1].SystemPath, b.Path); Assert.False(parent.IsDisposed);
    }
    [Theory]
    [InlineData(ArchiveEntryCollectionMode.CurrentDirectory)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubDirectories)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubArchives)]
    public async Task DeepExplicitImageAndHistoryUseLogicalPathAndReleaseWholeOwnedChain(ArchiveEntryCollectionMode mode)
    {
        using var f = new Fixture(); var middle = Outer(f, (f.Zip, "章节/内部.cbz")); var outer = Outer(f, (middle, "中间.cbz"));
        var path = Path.Combine(outer, "中间.cbz", "章节", "内部.cbz", "003.png"); var factory = new ArchiveFactory(); var baseline = NestedArchiveFile.ReservedBytes;
        Config.Current.System.ArchiveRecursiveMode = mode;
        var source = await factory.OpenAsync(path, Token);
        try
        {
            Assert.Equal("003.png", source.RequestedEntryName); Assert.Equal(Path.GetDirectoryName(path), source.Path); Assert.Equal(outer, source.RootArchivePath); Assert.Equal(2, source.NestingDepth);
            var entry = (await source.GetEntriesAsync(Token)).Single(e => e.EntryName == source.RequestedEntryName);
            Assert.Equal(path, entry.SystemPath); await using var stream = await source.OpenEntryAsync(entry, Token);
            Assert.Equal(400, (await new MagickImageDecoder().ProbeAsync(stream, Token)).Size.Width);
        }
        finally { await source.DisposeAsync(); }
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
        Assert.True(await factory.ExistsAsync(path, Token)); Assert.False(await factory.ExistsAsync(Path.Combine(Path.GetDirectoryName(path)!, "absent.png"), Token));
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
    }
    [Fact]
    public async Task ArchiveParentSegmentsCannotResolveToOutsideBookOrCreateExtractedNames()
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "../漫画.cbz")); var factory = new ArchiveFactory();
        await using var parent = await factory.OpenAsync(outer, Token); var entry = Assert.Single(await parent.GetEntriesAsync(Token));
        var baseline = NestedArchiveFile.ReservedBytes;
        await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenAsync(entry, Token));
        await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenAsync(outer + "/../漫画.cbz", Token));
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes); Assert.True(File.Exists(f.Zip));
    }
    [Theory]
    [InlineData(ArchiveEntryCollectionMode.CurrentDirectory)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubDirectories)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubArchives)]
    public async Task ParentBookFollowsOriginalArchiveCollectionMode(ArchiveEntryCollectionMode mode)
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "inside.cbz")); var state = new SaveData(f.State); await state.LoadAsync(Token);
        Config.Current.System.ArchiveRecursiveMode = mode; await using var operation = f.Operation(state);
        await operation.OpenAsync(Path.Combine(outer, "inside.cbz"), Token); Assert.Null(operation.Error);
        var expected = mode == ArchiveEntryCollectionMode.IncludeSubArchives ? f.Root : outer;
        Assert.Equal(expected, operation.Book!.BookAddress.Place);
        await operation.MoveToParentBookAsync(); Assert.Null(operation.Error); Assert.Equal(expected, operation.Book!.Path);
        Assert.Equal(mode == ArchiveEntryCollectionMode.IncludeSubArchives ? Path.GetFileName(outer) : "inside.cbz", operation.Book.CurrentPage!.EntryName);
    }
    [Fact]
    public async Task RestoreAfterCloseKeepsNestedBookAndOriginalPageName()
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "内部.cbz")); var nested = Path.Combine(outer, "内部.cbz");
        var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using (var operation = f.Operation(state))
        { await operation.OpenAsync(nested, Token); await operation.JumpAsync(3); await operation.SaveAsync(); Assert.Equal("004.png", state.GetLastBook()!.Page); }
        await state.LoadAsync(Token); await using var restored = f.Operation(state); await restored.RestoreLastAsync(Token);
        Assert.Null(restored.Error); Assert.Equal(nested, restored.Book!.Path); Assert.Equal("004.png", restored.Book.CurrentPage!.EntryName);
    }
    [Fact]
    public async Task OriginalInnerHistorySwitchAppliesToRealParentAndLeavesLogicalDirectoriesEnabled()
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "内部.cbz"), (Path.Combine(f.Images, "001.png"), "dir/001.png"));
        var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.History.IsInnerArchiveHistoryEnabled = false;
        await using var operation = f.Operation(state); await operation.OpenAsync(Path.Combine(outer, "内部.cbz"), Token);
        Assert.False(operation.Book!.MementoControl.CanHistory(Config.Current.History));
        Config.Current.History.IsInnerArchiveHistoryEnabled = true; Assert.True(operation.Book.MementoControl.CanHistory(Config.Current.History));
        Config.Current.History.IsInnerArchiveHistoryEnabled = false; await operation.OpenAsync(Path.Combine(outer, "dir"), Token);
        Assert.Null(operation.Error); Assert.Null(operation.Book!.Source.Parent); Assert.True(operation.Book.MementoControl.CanHistory(Config.Current.History));
    }
    [Fact]
    public async Task BadNestedArchiveAndMissingTargetDoNotLeakTemporaryMaterialOrReplaceCurrentBook()
    {
        using var f = new Fixture(); var bad = Path.Combine(f.Root, "bad.cbz"); await File.WriteAllTextAsync(bad, "invalid zip", Token);
        var outer = Outer(f, (bad, "坏.cbz"), (f.Zip, "valid.cbz")); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var operation = f.Operation(state); await operation.OpenAsync(f.Images, Token); var old = operation.Book; var baseline = NestedArchiveFile.ReservedBytes;
        await operation.OpenAsync(Path.Combine(outer, "坏.cbz"), Token); Assert.Same(old, operation.Book); Assert.NotNull(operation.Error); Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
        await operation.OpenAsync(Path.Combine(outer, "valid.cbz", "missing.png"), Token); Assert.Same(old, operation.Book); Assert.NotNull(operation.Error); Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
        await Assert.ThrowsAnyAsync<Exception>(() => new ArchiveFactory().ExistsAsync(Path.Combine(outer, "坏.cbz", "001.png"), Token));
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
    }
    [Fact]
    public async Task NestedZipIsReadOnlyEvenWhenRootZipDeletionIsEnabled()
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "inside.cbz")); var factory = new ArchiveFactory();
        Config.Current.Archive.Zip.IsFileWriteAccessEnabled = true;
        await using var source = await factory.OpenAsync(Path.Combine(outer, "inside.cbz"), Token); var entries = await source.GetEntriesAsync(Token);
        Assert.False(source.CanDelete([entries[0]])); Assert.Null(entries[0].FilePath); Assert.NotNull(source.CreateBookEntry().Archive); Assert.Null(source.CreateBookEntry().FilePath);
    }
    [Fact]
    public async Task NestedDepthBudgetAndPartialWriteAreBoundedAndRetryable()
    {
        var baseline = NestedArchiveFile.ReservedBytes;
        var first = new NestedArchiveFile(null); var second = new NestedArchiveFile(null);
        try
        {
            first.Reserve(NestedArchiveFile.Budget - baseline);
            Assert.Throws<NotSupportedException>(() => second.Reserve(1)); Assert.Equal(NestedArchiveFile.Budget, NestedArchiveFile.ReservedBytes);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); }
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
        var broken = new NestedArchiveFile(null);
        try { Assert.Throws<InvalidDataException>(() => broken.Write(new MemoryStream([1, 2]), 3, Token)); Assert.True(File.Exists(broken.Path)); }
        finally { await broken.DisposeAsync(); }
        Assert.False(File.Exists(broken.Path)); Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
        using var f = new Fixture(); var file = f.Zip; var path = file;
        for (var i = 0; i < ArchiveFactory.MaximumNestedDepth + 1; i++) { file = Outer(f, (file, "inner.cbz")); path = file + string.Concat(Enumerable.Repeat("/inner.cbz", i + 1)); }
        await Assert.ThrowsAsync<NotSupportedException>(() => new ArchiveFactory().OpenAsync(path, Token)); Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
    }
    [Fact]
    public async Task CancelledBackgroundWaitKeepsInputUntilActualWorkAndReleasesLateOutput()
    {
        using var release = new ManualResetEventSlim(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new TrackingStream(); var output = new TrackingStream(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var request = SourceIo.RunAsync(() => { started.SetResult(); release.Wait(Token); Assert.False(input.Closed.Task.IsCompleted); return output; }, cancel.Token, input);
        try
        {
            await started.Task.WaitAsync(Token); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.False(input.Closed.Task.IsCompleted); Assert.False(output.Closed.Task.IsCompleted);
        }
        finally { release.Set(); await input.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); await output.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); }
    }
    private sealed class TrackingStream : MemoryStream
    {
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Closed.TrySetResult(); }
    }
    [AvaloniaFact]
    public async Task FormalViewerLoadsNestedContentAndShutdownReturnsResources()
    {
        using var f = new Fixture(); var outer = Outer(f, (f.Zip, "内部.cbz")); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var operation = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show(); var baseline = NestedArchiveFile.ReservedBytes;
        try
        {
            await window.OpenAsync(outer); await window.Viewer.RefreshAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Null(operation.Error); Assert.Equal(5, operation.Book!.Pages.Count); Assert.True(NestedArchiveFile.ReservedBytes > baseline);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_NESTED_SCREENSHOT") is { } path)
            { Assert.True(Path.IsPathFullyQualified(path)); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(baseline, NestedArchiveFile.ReservedBytes);
    }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
