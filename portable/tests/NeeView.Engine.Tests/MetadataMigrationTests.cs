using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageMagick;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.Media.Imaging.Metadata;
namespace NeeView.Engine.Tests;

/// <summary>原EXIF/XMP优先级、搜索、唯一JSON及信息面板代次的真实后端回归。</summary>
public sealed class MetadataMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string MakeImage(Fixture fixture, string name, ushort? rating = 2)
    {
        var path = Path.Combine(fixture.Images, name);
        using var image = new MagickImage(MagickColors.Red, 80, 40);
        var exif = new ExifProfile(); exif.SetValue(ExifTag.ImageDescription, "原图标题"); exif.SetValue(ExifTag.Orientation, (ushort)6);
        exif.SetValue(ExifTag.Make, "NeeView Camera"); exif.SetValue(ExifTag.Model, "Mac");
        if (rating is { } value) exif.SetValue(ExifTag.Rating, value);
        image.SetProfile(exif); image.Orientation = OrientationType.RightTop;
        image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes("""
          <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/" xmp:Rating="5"><dc:subject><rdf:Bag><rdf:li>漫画</rdf:li><rdf:li>迁移</rdf:li></rdf:Bag></dc:subject></rdf:Description></rdf:RDF></x:xmpmeta>
          """)));
        image.Write(path); return path;
    }
    [Theory]
    [InlineData(false, true, 2)] [InlineData(true, true, 2)] [InlineData(false, false, 5)]
    public async Task RealExifXmpDirectoryAndZipKeepOriginalPriority(bool zip, bool ifdRating, int expected)
    {
        using var f = new Fixture(); MakeImage(f, "metadata.jpg", ifdRating ? (ushort)2 : null); var factory = new ArchiveFactory();
        var path = f.Images; if (zip) { path = Path.Combine(f.Root, "metadata.cbz"); ZipFile.CreateFromDirectory(f.Images, path); }
        await using var archive = await factory.OpenAsync(path, Token);
        var entry = (await archive.GetEntriesAsync(Token)).Single(e => e.EntryName == "metadata.jpg"); var page = new Page(entry, archives: factory);
        var info = await page.LoadPictureInfoAsync(Token); Assert.NotNull(info); Assert.Null(info.MetadataWarning);
        Assert.Equal(new NeeView.Size(40, 80), info.Image.Size); Assert.Equal(expected, PageMetadataTools.GetRating(page, Token));
        Assert.Equal("原图标题", PageMetadataTools.GetValueString(page, "title", Token));
        Assert.Equal("漫画; 迁移", PageMetadataTools.GetValueString(page, "tags", Token));
        Assert.Equal("NeeView Camera", PageMetadataTools.GetValueString(page, "CameraMaker", Token));
        Assert.NotEmpty(info.Metadata.ExtraMap); Assert.Same(info, await page.LoadPictureInfoAsync(Token));
        var standard = PageMetadataTools.GetValueStringMap(page, Token); Assert.Equal("原图标题", standard["Title"]);
    }
    [Fact]
    public async Task RealRatingAndMetadataSearchKeepOriginalSourcePagesAndPosition()
    {
        using var f = new Fixture(); MakeImage(f, "metadata.jpg"); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token); var book = op.Book!; var source = book.Pages.SourcePages.ToArray();
        Assert.True(await op.SearchPagesAsync("/rating /ge 2", book, Token)); var selected = Assert.Single(book.Pages);
        Assert.Equal("metadata.jpg", selected.EntryName); Assert.Same(selected, book.CurrentPage);
        Assert.True(await op.SearchPagesAsync("/tags 漫画", book, Token)); Assert.Same(selected, Assert.Single(book.Pages));
        await op.SearchPagesAsync("/title 不存在", book, Token); Assert.Empty(book.Pages); Assert.Same(selected, book.CurrentPage);
        await op.SearchPagesAsync("", book, Token); Assert.Equal(source, book.Pages.SourcePages); Assert.Same(selected, book.CurrentPage);
    }
    [Fact]
    public async Task InformationConfigKeepsRawNullGridLengthUnknownAndFailedSave()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), """{"Version":"46.3","Config":{"Information":{"PropertyHeaderWidth":"2*","DateTimeFormat":null,"MapProgramFormat":null,"IsVisibleExtras":true,"Future":7}}}""", Token);
        var state = new SaveData(f.State); await state.LoadAsync(Token); Assert.Equal("2*", Config.Current.Information.PropertyHeaderWidth);
        Assert.Null(Config.Current.Information.DateTimeFormatRaw); Assert.True(Config.Current.Information.IsVisibleExtras);
        await state.SaveAsync(null, Token); var raw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token))!;
        Assert.Equal(7, raw["Config"]!["Information"]!["Future"]!.GetValue<int>());
        var draft = new InformationSettingsViewModel(Config.Current.Information); draft.Draft.DateTimeFormat = "yyyy-MM-dd";
        await using var op = f.Operation(state); Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp"));
        await Assert.ThrowsAnyAsync<Exception>(() => op.ApplyOptionsAsync(() => draft.Apply(Config.Current.Information), (100, TimeSpan.Zero)));
        Assert.Null(Config.Current.Information.DateTimeFormatRaw); Assert.Equal("2*", Config.Current.Information.PropertyHeaderWidth);
        Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp"));
        await op.ApplyOptionsAsync(() => draft.Apply(Config.Current.Information), (100, TimeSpan.Zero)); Assert.Equal("yyyy-MM-dd", Config.Current.Information.DateTimeFormat);
    }
    [Fact]
    public async Task DamagedImageFailureIsRetryableAndDisposedSourceCannotPublish()
    {
        using var f = new Fixture(); var path = Path.Combine(f.Images, "broken.jpg"); await File.WriteAllTextAsync(path, "broken", Token);
        var factory = new ArchiveFactory(); await using var archive = await factory.OpenAsync(f.Images, Token);
        var entry = (await archive.GetEntriesAsync(Token)).Single(e => e.EntryName == "broken.jpg"); var page = new Page(entry, archives: factory);
        await Assert.ThrowsAnyAsync<Exception>(() => page.LoadPictureInfoAsync(Token));
        using (var image = new MagickImage(MagickColors.Blue, 30, 50)) image.Write(path);
        Assert.NotNull(await page.LoadPictureInfoAsync(Token)); await archive.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => page.LoadPictureInfoAsync(Token));
    }
    [Fact]
    public void MetadataReadBudgetCountsReadNotLargeFileLengthAndCancellation()
    {
        using var data = new MemoryStream(new byte[1024]); using var stream = new MetadataReadStream(data, 8, Token);
        stream.Seek(1000, SeekOrigin.Begin); Assert.Equal(8, stream.Read(new byte[10], 0, 10)); Assert.Throws<NotSupportedException>(() => stream.ReadByte());
        using var cancel = new CancellationTokenSource(); using var cancelled = new MetadataReadStream(data, 100, cancel.Token); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => cancelled.Seek(0, SeekOrigin.Begin));
    }
    [AvaloniaFact]
    public async Task OldNativeResultAndCancelledReadCannotReplaceNewSelection()
    {
        Config.SetCurrent(new()); var factory = new DelayedFactory();
        await using var source = new DummyArchive(); var first = new Page(new(source) { RawEntryName = "first.jpg" }, archives: factory);
        var second = new Page(new(source) { RawEntryName = "second.jpg" }, archives: factory);
        await using var book = new Book(source, [first, second], new()); book.CurrentPage = first;
        using var model = new FileInformationViewModel(); model.Update(book, true); var old = model.Loading;
        book.CurrentPage = second; model.Update(book, true); await model.Loading;
        factory.Gate.SetResult(new(new(new(20, 30), "JPEG"), new(new ValuesAccessor("旧标题")), "test")); await old;
        Assert.Same(second, model.Selected); Assert.Contains(model.Groups.SelectMany(g => g.Fields), r => r.Key == "Title" && r.Value == "新标题");
        Assert.DoesNotContain(model.Groups.SelectMany(g => g.Fields), r => r.Value == "旧标题");
        Assert.Equal("旧标题", (await first.LoadPictureInfoAsync(Token))!.Metadata[BitmapMetadataKey.Title]); Assert.Equal(3, factory.Calls);
    }
    [AvaloniaFact]
    public async Task OfficialInformationPanelReadsSamePageAndSettingsRemainDraft()
    {
        using var f = new Fixture(); MakeImage(f, "metadata.jpg"); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        await op.OpenAsync(Path.Combine(f.Images, "metadata.jpg"), Token); using var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        try
        {
            model.ShowPanel("FileInformationPanel"); await PageListThumbnailTests.SettleAsync(window); await model.FileInformation.Loading;
            Assert.Same(op.Book!.CurrentPage, model.FileInformation.Selected); Assert.Contains(model.FileInformation.Groups.SelectMany(g => g.Fields), r => r.Key == "Title" && r.Value == "原图标题");
            Assert.DoesNotContain(model.FileInformation.Groups.SelectMany(g => g.Fields), r => r.Group == InformationGroup.Extras);
            var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 13;
            Assert.True(settings.FindControl<ScrollViewer>("InformationSettings")!.IsVisible); settings.Close();
            Assert.Null(Config.Current.Information.DateTimeFormatRaw);
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p5-metadata";
            frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-information.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            await window.OpenAsync(f.Zip); await PageListThumbnailTests.SettleAsync(window); await model.FileInformation.Loading;
            Assert.DoesNotContain(model.FileInformation.Groups.SelectMany(g => g.Fields), r => r.Value == "原图标题");
        }
        finally { await window.PrepareShutdownAsync(); Assert.Equal(0, images.ByteCount); Assert.Equal(0, images.GetDiagnostics().Leases); window.Close(); }
    }
    private sealed class Platform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    private sealed class ValuesAccessor(string title) : BitmapMetadataAccessor
    {
        public override string GetFormat() => "JPEG";
        public override object? GetValue(BitmapMetadataKey key) => key == BitmapMetadataKey.Title ? title : null;
    }
    private sealed class DummyArchive() : Archive("dummy")
    {
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ArchiveEntry>>([]);
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class DelayedFactory : IArchiveFactory
    {
        public TaskCompletionSource<PagePictureInfo> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public Task<PagePictureInfo> ReadImageMetadataAsync(ArchiveEntry entry, CancellationToken token)
        {
            Calls++;
            return entry.EntryName == "first.jpg" ? Gate.Task : Task.FromResult(new PagePictureInfo(new(new(20, 30), "JPEG"), new(new ValuesAccessor("新标题")), "test"));
        }
        public Task<Archive> OpenAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => throw new NotSupportedException();
    }
}
