using ImageMagick;
using NeeView.Application;
using NeeView.Content;
using NeeView.Core;
using NeeView.Desktop;
using NeeView.Imaging;
using NeeView.Persistence;
using Xunit;

#pragma warning disable xUnit1051
namespace NeeView.Portable.Tests;

public sealed class ProgressiveTests
{
    /// <summary>历史位置位于后续批次时仍按身份恢复，不能在首批中被重置。</summary>
    [Fact]
    public async Task ProgressiveDirectoryRestoresAnchorBeyondInitialBatch()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "large"); Directory.CreateDirectory(folder);
        for (var i = 0; i < 300; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"{i:000}.jpg"), "image");
        var book = await workspace.States.GetBookAsync(new(folder), default);
        var target = await workspace.States.GetContentAsync(new(Path.Combine(folder, "299.jpg")), default);
        await workspace.States.SaveAsync(new(book, new(folder), new(target, 0, 0.4), new(), DateTimeOffset.UtcNow));
        await using var session = workspace.Session(); await session.OpenAsync(new(folder));
        Assert.Equal(new ReadingAnchor(target, 0, 0.4), session.Snapshot.Anchor);
        Assert.Equal("299.jpg", session.Snapshot.Current!.Name);
    }
    /// <summary>单图批次无需完成目录索引即可返回；最终自然排序保持选中图片身份。</summary>
    [Fact]
    public async Task RequestedImageIsPublishedBeforeDirectoryEnumerationCompletes()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "images"); Directory.CreateDirectory(folder);
        for (var i = 0; i < 260; i++) await File.WriteAllTextAsync(Path.Combine(folder, i + ".jpg"), "image");
        await using var source = new DirectorySource(folder, Path.Combine(folder, "259.jpg"), workspace.States);
        await using var batches = source.IndexBatchesAsync(default).GetAsyncEnumerator();
        Assert.True(await batches.MoveNextAsync()); var first = batches.Current; Assert.Single(first.Pages); Assert.Equal("259.jpg", first.Pages[0].Name);
        var final = first; var count = 1; while (await batches.MoveNextAsync()) { final = batches.Current; count++; }
        Assert.True(count >= 4); Assert.Equal(260, final.Pages.Count); Assert.Equal(first.RequestedContent, final.RequestedContent);
        Assert.Equal("2.jpg", final.Pages[2].Name);
    }
    /// <summary>磁盘缩略图在调度器重建后仍可使用，版本改变时不会复用旧像素。</summary>
    [Fact]
    public async Task ThumbnailSurvivesSchedulerRestartAndVersionIsPartOfKey()
    {
        await using var workspace = new TestWorkspace(); var cache = new DiskThumbnailCache(Path.Combine(workspace.Root, "thumbnails"), 528);
        var firstDecoder = new FakeDecoder(); var source = new FakeSource(); var page = FakeSource.SamplePage();
        await using (var scheduler = new ImageScheduler(firstDecoder, thumbnails: cache)) { using var image = await scheduler.RequestAsync(source, new(page, 96, 128, true), ImagePriority.Thumbnail, default); }
        var secondDecoder = new FakeDecoder();
        await using (var scheduler = new ImageScheduler(secondDecoder, thumbnails: cache))
        {
            using var restored = await scheduler.RequestAsync(source, new(page, 96, 128, true), ImagePriority.Thumbnail, default); Assert.Equal(0, secondDecoder.Calls);
            using var changed = await scheduler.RequestAsync(source, new(page with { Version = new(101, 2) }, 96, 128, true), ImagePriority.Thumbnail, default); Assert.Equal(1, secondDecoder.Calls);
        }
        Assert.True(Directory.EnumerateFiles(Path.Combine(workspace.Root, "thumbnails")).Sum(p => new FileInfo(p).Length) <= 528);
    }
    /// <summary>JPEG 目标大于原图时不得使用 hint 导致放大。</summary>
    [Fact]
    public async Task JpegDecodeNeverUpscalesSmallOriginal()
    {
        using var original = new MagickImage(MagickColors.Red, 384, 512);
        await using var stream = new MemoryStream(original.ToByteArray(MagickFormat.Jpeg));
        using var image = await new MagickImageDecoder().DecodeAsync(stream, new(FakeSource.SamplePage(), 2048, 4096), default);
        Assert.Equal(new(384, 512), image.Size);
    }
    /// <summary>旧单图历史与书签转换成目录书籍，打开后定位原图片。</summary>
    [Fact]
    public async Task ImportedSingleImageUsesDirectoryBookIdentity()
    {
        await using var workspace = new TestWorkspace(); var folder = Path.Combine(workspace.Root, "pictures"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "2.jpg"), "image"); await File.WriteAllTextAsync(Path.Combine(folder, "1.jpg"), "image");
        var profile = Path.Combine(workspace.Root, "profile"); Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "History.json"), """{"Items":[{"Path":"C:\\Images\\2.jpg"}]}""");
        await File.WriteAllTextAsync(Path.Combine(profile, "Bookmark.json"), """{"Books":[{"Name":"单图","Path":"C:\\Images\\2.jpg"}]}""");
        var importer = new LegacyImporter(workspace.States, workspace.Settings); await importer.ApplyAsync(await importer.PlanImportAsync(profile, [new("C:\\Images", folder)]));
        var history = Assert.Single(await workspace.States.HistoryAsync()); Assert.Equal(folder, history.Locator.Path); Assert.Equal("2.jpg", history.LegacyPage);
        var bookmark = Assert.Single(await workspace.States.BookmarksAsync()); Assert.Equal(history.Book, bookmark.Book); Assert.Equal("2.jpg", bookmark.LegacyPage);
        await using var session = workspace.Session(); await session.OpenAsync(new(folder)); Assert.Equal("2.jpg", session.Snapshot.Current!.Name);
    }
    /// <summary>映射别名及不同参数的同一键位均必须报告冲突。</summary>
    [Fact]
    public void ShortcutAliasesAndParametersCannotSilentlyOverwrite()
    {
        Assert.Single(CommandCatalog.Conflicts([new("Control+Right", "NextPage"), new("Ctrl+Right", "PrevPage")]));
        Assert.Single(CommandCatalog.Conflicts([new("1", "MoveToDestinationFolder1", "1"), new("1", "MoveToDestinationFolder1", "2")]));
        Assert.True(ReaderInputRouter.TryGesture("Control+1", out _));
    }
}
