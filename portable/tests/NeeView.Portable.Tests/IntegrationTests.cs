using System.IO.Compression;
using ImageMagick;
using NeeView.Application;
using NeeView.Content;
using NeeView.Core;
using NeeView.Imaging;
using NeeView.Persistence;
using Xunit;

// 集成夹具检验应用自己的取消/关闭责任，使用独立 token，避免测试运行器取消掩盖资源清理结果。
#pragma warning disable xUnit1051

namespace NeeView.Portable.Tests;

public sealed class IntegrationTests
{
    /// <summary>未单独配置的恢复字段使用历史值；显式默认策略仍可覆盖每本书保存的模式。</summary>
    [Fact]
    public async Task RestartRestoresBookModeAndExplicitDefaultStillWins()
    {
        await using var workspace = new TestWorkspace();
        var folder = Path.Combine(workspace.Root, "mode-restore"); Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "1.jpg"), [1]);
        // 模拟设置页仅写入部分策略，Mode 没有配置项时不能误用枚举零值 Default。
        await workspace.Settings.SaveAsync(new() { RestorePolicies = new() { [nameof(ReaderOptions.Zoom)] = RestorePolicy.Continue } });
        await using (var initial = workspace.Session())
        {
            await initial.OpenAsync(new(folder));
            await initial.SetOptionsAsync(initial.Snapshot.Options with { Mode = ReaderMode.Masonry });
        }
        await using (var restored = workspace.Session())
        {
            await restored.OpenAsync(new(folder)); Assert.Equal(ReaderMode.Masonry, restored.Snapshot.Options.Mode);
        }
        await workspace.Settings.UpdateAsync(s => s with { RestorePolicies = new() { [nameof(ReaderOptions.Mode)] = RestorePolicy.Default } });
        await using var defaultMode = workspace.Session(); await defaultMode.OpenAsync(new(folder));
        Assert.Equal(ReaderMode.Paged, defaultMode.Snapshot.Options.Mode);
    }
    [Fact]
    public async Task DirectoryZipNavigationRestoreAndFailureKeepCurrentBook()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "pictures"); Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "2.jpg"), [2]); await File.WriteAllBytesAsync(Path.Combine(folder, "10.jpg"), [10]);
        await using var session = workspace.Session(); await session.OpenAsync(new(Path.Combine(folder, "10.jpg")));
        Assert.Equal("10.jpg", session.Snapshot.Current!.Name);
        var selected = session.Snapshot.Anchor; await session.FlushAsync();
        await session.OpenAsync(new("/this-never-exists/neeview")); Assert.Equal(selected, session.Snapshot.Anchor); Assert.NotNull(session.Snapshot.Error);
        await using var restored = workspace.Session(); await restored.OpenAsync(new(folder)); Assert.Equal(selected, restored.Snapshot.Anchor);
        var archivePath = Path.Combine(workspace.Root, "book.cbz"); ZipFile.CreateFromDirectory(folder, archivePath);
        await session.OpenAsync(new(archivePath)); Assert.True(session.Snapshot.Index!.Capabilities.IsArchive);
        await session.SetOptionsAsync(session.Snapshot.Options with { Sort = SortMode.FileName });
        await session.LocateAsync(new(session.Snapshot.Index.Pages[0].Id));
        await using var stream = await session.Source!.OpenReadAsync(session.Snapshot.Current!, default); Assert.Equal(2, stream.ReadByte());
        await session.NavigateAsync(1, true); Assert.Equal("10.jpg", session.Snapshot.Current!.Name);
    }
    [Fact]
    public async Task SortingRetainsContentAndEmptyDirectoryIsRecognized()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "images"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "1.png"), "one"); await File.WriteAllTextAsync(Path.Combine(folder, "2.png"), "two");
        await using var session = workspace.Session(); await session.OpenAsync(new(folder)); var anchor = session.Snapshot.Anchor;
        await session.SetOptionsAsync(session.Snapshot.Options with { Sort = SortMode.FileNameDescending }); Assert.Equal(anchor, session.Snapshot.Anchor);
        var empty = Path.Combine(workspace.Root, "empty"); Directory.CreateDirectory(empty); await session.OpenAsync(new(empty));
        Assert.Empty(session.Snapshot.Index!.Pages); Assert.Null(session.Snapshot.Anchor); Assert.Null(session.Snapshot.Error);
    }
    [Fact]
    public async Task DuplicateImageRequestsMergeAndCancelledWaiterDoesNotCancelOthers()
    {
        var decoder = new FakeDecoder { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var scheduler = new ImageScheduler(decoder, 256, 256); var source = new FakeSource(); var page = FakeSource.SamplePage();
        using var cancellation = new CancellationTokenSource();
        var first = scheduler.RequestAsync(source, new(page), ImagePriority.Current, cancellation.Token);
        var second = scheduler.RequestAsync(source, new(page), ImagePriority.Visible, default);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        decoder.Gate.SetResult(); using var image = await second;
        Assert.Equal(1, decoder.Calls); Assert.Equal(256, image.ByteCount);
    }
    [Fact]
    public async Task CacheBudgetNeverDisposesLiveLease()
    {
        await using var scheduler = new ImageScheduler(new FakeDecoder(), 256, 256); var source = new FakeSource();
        using var first = await scheduler.RequestAsync(source, new(FakeSource.SamplePage("1")), ImagePriority.Current, default);
        using (var second = await scheduler.RequestAsync(source, new(FakeSource.SamplePage("2")), ImagePriority.Current, default)) Assert.Equal(512, scheduler.CachedBytes);
        Assert.Equal(256, first.Pixels.Length); Assert.Equal(256, scheduler.CachedBytes);
    }
    [Fact]
    public async Task NativeDecodeProducesPremultipliedPixelsAndRejectsCorruptImage()
    {
        var decoder = new MagickImageDecoder();
        using var original = new MagickImage(new MagickColor(255, 0, 0, 128), 80, 120);
        await using var stream = new MemoryStream(original.ToByteArray(MagickFormat.Png));
        var info = await decoder.ProbeAsync(stream, default); Assert.Equal(new(80, 120), info.Size); stream.Position = 0;
        using var image = await decoder.DecodeAsync(stream, new(FakeSource.SamplePage(), 40, 60), default);
        Assert.Equal(new(40, 60), image.Size); Assert.Equal(128, image.Pixels.Span[3]); Assert.InRange((int)image.Pixels.Span[2], 126, 129);
        await using var corrupt = new MemoryStream([1, 2, 3]); await Assert.ThrowsAnyAsync<Exception>(() => decoder.DecodeAsync(corrupt, new(FakeSource.SamplePage()), default));
    }
    [Fact]
    public async Task MoveCopyUndoRedoAndConflictCancellationPreserveIdentity()
    {
        await using var workspace = new TestWorkspace(); var source = Path.Combine(workspace.Root, "source"); var target = Path.Combine(workspace.Root, "target");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target); var file = Path.Combine(source, "test.jpg"); await File.WriteAllTextAsync(file, "original");
        var page = await workspace.PageAsync(file); var service = workspace.FileService();
        var copied = await service.ExecuteAsync(page, FileActionKind.Copy, target); Assert.True(copied.Success); Assert.True(File.Exists(file)); Assert.False(service.CanUndo);
        var cancelled = await service.ExecuteAsync(page, FileActionKind.Move, target); Assert.False(cancelled.Success); Assert.False(service.CanUndo);
        await File.WriteAllTextAsync(Path.Combine(target, "test.jpg"), "overwritten");
        var moved = await service.ExecuteAsync(page, FileActionKind.Move, target, ConflictChoice.Overwrite); Assert.True(moved.Success); Assert.False(File.Exists(file)); Assert.True(service.CanUndo);
        Assert.Equal(page.Id, await workspace.States.GetContentAsync(new(moved.Target!), default));
        var undone = await service.UndoAsync(); Assert.True(undone.Success); Assert.Equal("original", await File.ReadAllTextAsync(file)); Assert.Equal("overwritten", await File.ReadAllTextAsync(moved.Target!)); Assert.True(service.CanRedo);
        var redoCancelled = await service.RedoAsync(); Assert.False(redoCancelled.Success); Assert.True(service.CanRedo);
        var redone = await service.RedoAsync(ConflictChoice.Overwrite); Assert.True(redone.Success); Assert.True(service.CanUndo); Assert.False(File.Exists(file));
        Assert.Empty(await workspace.States.RecoveriesAsync());
    }
    [Fact]
    public async Task MoveFailureDoesNotPopHistoryAndCapacityIsBounded()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "dest"); Directory.CreateDirectory(folder); var service = workspace.FileService(); service.Capacity = 1;
        for (var i = 0; i < 2; i++) { var path = Path.Combine(workspace.Root, i + ".jpg"); await File.WriteAllTextAsync(path, i.ToString()); Assert.True((await service.ExecuteAsync(await workspace.PageAsync(path), FileActionKind.Move, folder)).Success); }
        File.Delete(Path.Combine(folder, "1.jpg")); Assert.False((await service.UndoAsync()).Success); Assert.True(service.CanUndo);
        await File.WriteAllTextAsync(Path.Combine(folder, "1.jpg"), "1"); Assert.True((await service.UndoAsync()).Success); Assert.False(service.CanUndo); Assert.True(service.CanRedo);
        service.Capacity = 0; Assert.False(service.CanRedo);
    }
    [Fact]
    public async Task DestinationChildrenAreTransientAndRefreshOnlyOnDirectoryChange()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "source"); Directory.CreateDirectory(folder);
        await workspace.Settings.SaveAsync(new() { DestinationFolders = Enumerable.Range(0, 12).Select(i => "/dest/" + i).ToList() });
        var service = new DestinationFolderService(workspace.Settings); await service.RefreshAsync(folder, false);
        Directory.CreateDirectory(Path.Combine(folder, "child")); await service.RefreshAsync(folder, false); Assert.Empty(service.Children); Assert.Equal(12, service.Managed.Count);
        await service.RefreshAsync(folder, true); Assert.Single(service.Children); Assert.Equal(12, (await workspace.Settings.LoadAsync()).DestinationFolders.Count);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateChildAsync(folder, "../escape"));
    }
    [Fact]
    public async Task LegacyZipImportsTreePagePathsAndControlWithoutChangingSource()
    {
        await using var workspace = new TestWorkspace(); var source = Path.Combine(workspace.Root, "old.nvzip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            foreach (var (name, json) in new[] {
                ("History.json", """{"Items":[{"Path":"C:\\Books\\book.cbz","Page":"001.jpg","Props":"WidePage LeftToRight IsSingleFirst Base=1.25"}]}"""),
                ("Bookmark.json", """{"Nodes":{"Children":[{"Name":"漫画","Children":[{"Name":"示例","Path":"C:\\Books\\book.cbz","Page":"002.jpg"}]}]}}"""),
                ("UserSetting.json", """{"Commands":{"NextPage":{"ShortCutKey":"Control+Right,WheelDown"},"Unsupported":{"ShortCutKey":"F10"}}}"""),
                ("Foldres.json", "{}"), ("QuicAccess.json", "{}") })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(json); }
        }
        var before = await File.ReadAllBytesAsync(source); var importer = new LegacyImporter(workspace.States, workspace.Settings);
        var plan = await importer.PlanImportAsync(source, [new("C:\\Books", "/Volumes/manga"), new("C:\\Books\\nested", "/Volumes/special")]);
        Assert.Equal("/Volumes/manga/book.cbz", plan.Books[0].Path); Assert.Equal("001.jpg", plan.Books[0].Page); Assert.True(plan.Books[0].Options.DoublePage);
        Assert.Contains(plan.Settings.Shortcuts, b => b.Gesture == "Control+Right"); Assert.Contains(plan.Warnings, w => w.Contains("Unsupported"));
        await importer.ApplyAsync(plan); var marks = await workspace.States.BookmarksAsync(); Assert.Equal(3, marks.Count); Assert.Contains(marks, m => m.Name == "示例" && m.LegacyPage == "002.jpg" && m.ParentId is not null);
        var state = Assert.Single(await workspace.States.HistoryAsync()); Assert.Equal("001.jpg", state.LegacyPage); Assert.Equal(before, await File.ReadAllBytesAsync(source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ApplyAsync(plan));
        Assert.Equal("/Volumes/special/test.png", LegacyImporter.MapPath("c:/books/nested/test.png", [new("C:\\Books", "/Volumes/manga"), new("C:\\Books\\nested", "/Volumes/special")]));
        Assert.Equal("C:\\Bookshop\\test.png", LegacyImporter.MapPath("C:\\Bookshop\\test.png", [new("C:\\Books", "/Volumes/manga")]));
    }
}
