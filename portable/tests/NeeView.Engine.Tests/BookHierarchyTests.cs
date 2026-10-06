using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

public sealed class BookHierarchyTests
{
    /// <summary>原默认收集模式保留真实目录/归档，Image与All也保持原枚举语义。</summary>
    [Theory]
    [InlineData(BookPageCollectMode.Image, 1)]
    [InlineData(BookPageCollectMode.ImageAndBook, 5)]
    [InlineData(BookPageCollectMode.All, 6)]
    public async Task OriginalCollectionModesKeepBookPages(BookPageCollectMode mode, int count)
    {
        using var fixture = new Fixture(); var library = Library(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.System.BookPageCollectMode = mode;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(library, TestContext.Current.CancellationToken);
        Assert.Null(operation.Error); Assert.Equal(count, operation.Book!.Pages.Count);
        Assert.Single(operation.Book.Pages, p => p.IsImage);
        if (mode != BookPageCollectMode.Image)
        {
            var child = operation.Book.Pages.Single(p => p.EntryName == "child");
            Assert.Equal(PageType.Folder, child.PageType); Assert.Equal(new NeeView.Size(480, 640), child.Content.PageDataSource.Size);
            Assert.True(child.Content.HasSize); Assert.True(child.Content.IsFileContent);
            Assert.Equal(PageType.Archive, operation.Book.Pages.Single(p => p.EntryName == "book.cbz").PageType);
        }
    }
    /// <summary>父/子书按真实页面往返，和独立书架的浏览/选择无关；空子书仍可返回。</summary>
    [Fact]
    public async Task ParentChildRoundTripIgnoresBookshelfSelection()
    {
        using var fixture = new Fixture(); var library = Library(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(Path.Combine(library, "child"), TestContext.Current.CancellationToken);
        await operation.Bookshelf.SetPlaceAsync(fixture.Images, token: TestContext.Current.CancellationToken);
        await operation.MoveToParentBookAsync(); Assert.Null(operation.Error);
        Assert.Equal(library, operation.Book!.Path); Assert.Equal("child", operation.Book.CurrentPage!.EntryName); Assert.True(operation.CanMoveToChildBook);
        await operation.MoveToChildBookAsync(); Assert.Equal(Path.Combine(library, "child"), operation.Book!.Path);
        await operation.MoveToParentBookAsync();
        await operation.OpenChildBookAsync(operation.Book!.Pages.Single(p => p.EntryName == "empty"), operation.Book);
        Assert.Empty(operation.Book!.Pages); Assert.Null(operation.Frame); Assert.True(operation.CanMoveToParentBook); Assert.False(operation.CanMoveToChildBook);
        await operation.MoveToParentBookAsync(); Assert.Equal("empty", operation.Book!.CurrentPage!.EntryName);
        await operation.OpenChildBookAsync(operation.Book.Pages.Single(p => p.EntryName == "book.cbz"), operation.Book);
        Assert.Equal(Path.Combine(library, "book.cbz"), operation.Book!.Path);
        await operation.MoveToParentBookAsync(); Assert.Equal("book.cbz", operation.Book!.CurrentPage!.EntryName);
    }
    /// <summary>Mac合法反斜杠名称必须保持，父导航不能把它改成目录分隔符。</summary>
    [Fact]
    public async Task ParentBookKeepsLiteralBackslashName()
    {
        using var fixture = new Fixture(); var parent = Path.Combine(fixture.Root, "parent"); var child = Path.Combine(parent, @"chapter\one"); Directory.CreateDirectory(child);
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(child, "001.png"));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(child, TestContext.Current.CancellationToken); await operation.MoveToParentBookAsync();
        Assert.Null(operation.Error); Assert.Equal(@"chapter\one", operation.Book!.CurrentPage!.EntryName);
        await operation.MoveToChildBookAsync(); Assert.Equal(child, operation.Book!.Path);
    }
    /// <summary>递归展平有内容的书籍，保留空书/坏包/shortcut，页面读取仍用各自所属来源。</summary>
    [Fact]
    public async Task RecursiveCollectionOwnsChildrenAndSkipsSymlinkLoop()
    {
        using var fixture = new Fixture(); var library = Library(fixture);
        Directory.CreateSymbolicLink(Path.Combine(library, "loop"), library);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(library, TestContext.Current.CancellationToken);
        var original = operation.Book; await operation.ToggleRecursiveFolderAsync(); Assert.Null(operation.Error);
        Assert.True(original!.Source.IsDisposed); Assert.True(operation.Book!.Setting.IsRecursiveFolder);
        Assert.Contains(operation.Book.Pages, p => p.EntryName == "child/002.png"); Assert.Contains(operation.Book.Pages, p => p.EntryName == "book.cbz/005.png");
        Assert.DoesNotContain(operation.Book.Pages, p => p.EntryName is "child" or "book.cbz");
        Assert.Contains(operation.Book.Pages, p => p.EntryName == "empty"); Assert.Contains(operation.Book.Pages, p => p.EntryName == "bad.cbz");
        Assert.Contains(operation.Book.Pages, p => p.EntryName == "loop" && p.ArchiveEntry.IsShortcut);
        var nested = operation.Book.Pages.Single(p => p.EntryName == "book.cbz/003.png"); var childSource = nested.ArchiveEntry.Archive;
        using var cache = new BitmapFactory(new MagickImageDecoder()); using (var image = await cache.GetAsync(nested, new(80, 120), TestContext.Current.CancellationToken)) Assert.Equal(80 * 120 * 4, image.Image.ByteCount);
        await operation.JumpAsync(nested.Index); await operation.ToggleRecursiveFolderAsync(); Assert.Null(operation.Error);
        Assert.True(childSource.IsDisposed); Assert.Equal("book.cbz", operation.Book!.CurrentPage!.EntryName);
    }
    /// <summary>ZIP显式/隐式/空目录与同前缀目录隔离；当前目录模式逐层返回。</summary>
    [Fact]
    public async Task ZipCurrentDirectoryAndParentRestorePhysicalEntry()
    {
        using var fixture = new Fixture(); var zip = Chapters(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(zip, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "chapter", "chapter-long", "empty", "001.png" }, operation.Book!.Pages.Select(p => p.EntryName));
        await operation.OpenChildBookAsync(operation.Book.Pages.Single(p => p.EntryName == "chapter"), operation.Book);
        Assert.Equal(Path.Combine(zip, "chapter"), operation.Book!.Path);
        Assert.Equal(new[] { "deep", "002.png" }, operation.Book.Pages.Select(p => p.EntryName));
        await operation.OpenAsync(Path.Combine(zip, "chapter/deep/003.png"), TestContext.Current.CancellationToken);
        Assert.Null(operation.Error); Assert.Equal("003.png", operation.Book!.CurrentPage!.EntryName);
        using var cache = new BitmapFactory(new MagickImageDecoder()); using var image = await cache.GetAsync(operation.Book.CurrentPage, new(80, 120), TestContext.Current.CancellationToken);
        Assert.Equal(80 * 120 * 4, image.Image.ByteCount);
        Assert.True(await operation.Bookshelf.SyncAsync(operation.Book, TestContext.Current.CancellationToken)); Assert.Null(operation.Bookshelf.Error);
        Assert.Equal(Path.Combine(zip, "chapter"), operation.Bookshelf.Place); Assert.Equal("deep", operation.Bookshelf.SelectedItem!.Name);
        await operation.MoveToParentBookAsync(); Assert.Equal("deep", operation.Book!.CurrentPage!.EntryName);
        await operation.MoveToParentBookAsync(); Assert.Equal("chapter", operation.Book!.CurrentPage!.EntryName);
        await operation.OpenAsync(Path.Combine(zip, "chapter-long"), TestContext.Current.CancellationToken); Assert.Single(operation.Book!.Pages); Assert.Equal("004.png", operation.Book.CurrentPage!.EntryName);
        await operation.OpenAsync(Path.Combine(zip, "empty"), TestContext.Current.CancellationToken); Assert.Empty(operation.Book!.Pages); await operation.MoveToParentBookAsync(); Assert.Equal("empty", operation.Book!.CurrentPage!.EntryName);
    }
    /// <summary>归档非当前目录模式保留后代图片，真实父书回根归档所在目录，并定位归档页。</summary>
    [Theory]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubDirectories)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubArchives)]
    public async Task ArchiveRecursiveModesReturnRootArchiveParent(ArchiveEntryCollectionMode mode)
    {
        using var fixture = new Fixture(); var zip = Chapters(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); Config.Current.System.ArchiveRecursiveMode = mode;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(Path.Combine(zip, "chapter/002.png"), TestContext.Current.CancellationToken);
        Assert.Equal(zip, operation.Book!.Path); Assert.Equal("chapter/002.png", operation.Book.CurrentPage!.EntryName);
        Assert.Contains(operation.Book.Pages, p => p.EntryName == "chapter/deep/003.png");
        await operation.OpenAsync(Path.Combine(zip, "chapter"), TestContext.Current.CancellationToken); Assert.Equal(2, operation.Book!.Pages.Count);
        Assert.True(await operation.Bookshelf.SyncAsync(operation.Book, TestContext.Current.CancellationToken)); Assert.Equal(zip, operation.Bookshelf.SelectedItem!.Path);
        await operation.MoveToParentBookAsync(); Assert.Null(operation.Error); Assert.Equal(fixture.Root, operation.Book!.Path); Assert.Equal("chapters.cbz", operation.Book.CurrentPage!.EntryName);
    }
    /// <summary>子包/父书打开失败保留旧来源和位置，修复后相同入口可以重试。</summary>
    [Fact]
    public async Task FailedChildAndParentKeepCurrentBookForRetry()
    {
        using var fixture = new Fixture(); var library = Library(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(library, TestContext.Current.CancellationToken); var old = operation.Book;
        await operation.OpenChildBookAsync(old!.Pages.Single(p => p.EntryName == "bad.cbz"), old); Assert.Same(old, operation.Book); Assert.NotNull(operation.Error); Assert.False(old.Source.IsDisposed);
        File.Copy(fixture.Zip, Path.Combine(library, "bad.cbz"), true);
        await operation.OpenChildBookAsync(old.Pages.Single(p => p.EntryName == "bad.cbz"), old); Assert.Null(operation.Error);
        Config.Current.System.BookPageCollectMode = BookPageCollectMode.Image;
        var child = operation.Book; await operation.MoveToParentBookAsync(); Assert.Same(child, operation.Book); Assert.NotNull(operation.Error);
        Config.Current.System.BookPageCollectMode = BookPageCollectMode.ImageAndBook;
        await operation.MoveToParentBookAsync(); Assert.Null(operation.Error); Assert.Equal("bad.cbz", operation.Book!.CurrentPage!.EntryName);
    }
    /// <summary>P5真实包内归档进入子书并可返回父包，不能回根包冒充子书打开。</summary>
    [Fact]
    public async Task NestedArchiveOpensChildAndRestoresParentEntry()
    {
        using var fixture = new Fixture(); var zip = Path.Combine(fixture.Root, "nested.cbz");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(fixture.Zip, "inside.cbz");
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory;
        await operation.OpenAsync(zip, TestContext.Current.CancellationToken); await operation.MoveToChildBookAsync();
        Assert.Null(operation.Error); Assert.Equal(Path.Combine(zip, "inside.cbz"), operation.Book!.Path); Assert.Equal(5, operation.Book.Pages.Count);
        await operation.MoveToParentBookAsync(); Assert.Null(operation.Error); Assert.Equal(zip, operation.Book!.Path); Assert.Equal("inside.cbz", operation.Book.CurrentPage!.EntryName);
    }
    /// <summary>封面首图优先/regex/原有限子书深度与显式目标，读取后关闭请求级来源。</summary>
    [Fact]
    public async Task CoversHonorOriginalDepthRegexAndFolderOverride()
    {
        using var fixture = new Fixture(); var library = Library(fixture);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(library, TestContext.Current.CancellationToken); var child = operation.Book!.Pages.Single(p => p.EntryName == "child");
        File.Copy(Path.Combine(fixture.Images, "003.png"), Path.Combine(library, "child", "folder.jpg"));
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(child, TestContext.Current.CancellationToken)) Assert.Equal("folder.jpg", cover.Entry!.EntryName);
        Config.Current.Book.BookThumbnailRegex = "[invalid";
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(child, TestContext.Current.CancellationToken)) Assert.Equal("002.png", cover.Entry!.EntryName);
        var empty = operation.Book.Pages.Single(p => p.EntryName == "empty"); var deeper = Path.Combine(library, "empty", "deeper"); Directory.CreateDirectory(deeper);
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(deeper, "001.png")); Config.Current.Book.BookThumbnailDepth = 1;
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(empty, TestContext.Current.CancellationToken)) Assert.Null(cover.Entry);
        Config.Current.Book.BookThumbnailDepth = 2; Archive? owned;
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(empty, TestContext.Current.CancellationToken)) { owned = cover.Entry!.Archive; Assert.Equal("001.png", cover.Entry.EntryName); }
        Assert.True(owned.IsDisposed);
        state.FolderConfigs.Restore(new JsonObject { ["Folders"] = new JsonArray(new JsonObject { ["Place"] = library, ["Thumbs"] = new JsonObject { ["child"] = "../001.png" } }) });
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(child, TestContext.Current.CancellationToken)) Assert.Equal(Path.Combine(library, "001.png"), cover.Entry!.SystemPath);
        Assert.Equal("../001.png", state.FolderConfigs.CreateMemento()["Folders"]![0]!["Thumbs"]!["child"]!.GetValue<string>());
    }
    /// <summary>原ZIP目录封面在整个前缀范围找首图；深度1并非限制已有包内后代。</summary>
    [Fact]
    public async Task ArchiveDirectoryCoverUsesStrictPrefixAndWholeRange()
    {
        using var fixture = new Fixture(); var zip = Chapters(fixture); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory; Config.Current.Book.BookThumbnailDepth = 1;
        await using var operation = fixture.Operation(state); await operation.OpenAsync(zip, TestContext.Current.CancellationToken);
        await using var cover = await ArchivePageUtility.GetSelectedPageAsync(operation.Book!.Pages.Single(p => p.EntryName == "chapter"), TestContext.Current.CancellationToken);
        Assert.StartsWith("chapter/", cover.Entry!.EntryName); Assert.DoesNotContain("chapter-long", cover.Entry.EntryName);
        using var cache = new BitmapFactory(new MagickImageDecoder());
        using var image = await cache.GetAsync(operation.Book.Pages.Single(p => p.EntryName == "chapter"), new(80, 120), TestContext.Current.CancellationToken);
        Assert.Equal(new NeeView.Size(80, 120), image.Image.Size);
    }
    /// <summary>原Take(depth)同样限制候选子书数量，不能为了寻找封面扫描整个树。</summary>
    [Fact]
    public async Task CoverDepthBoundsNumberOfSubBooksAndReleasesEmptySources()
    {
        using var fixture = new Fixture(); var library = Library(fixture); var empty = Path.Combine(library, "empty");
        foreach (var name in new[] { "A", "B", "C" }) Directory.CreateDirectory(Path.Combine(empty, name));
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(empty, "C", "001.png"));
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var factory = new TrackingFactory(); await using var operation = new BookOperation(factory, new MagickImageDecoder(), state);
        await operation.OpenAsync(library, TestContext.Current.CancellationToken); var page = operation.Book!.Pages.Single(p => p.EntryName == "empty");
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(page, TestContext.Current.CancellationToken)) Assert.Null(cover.Entry);
        Assert.Equal(4, factory.Sources.Count); Assert.All(factory.Sources.Skip(1), source => Assert.True(source.IsDisposed));
        Config.Current.Book.BookThumbnailDepth = 3;
        await using (var cover = await ArchivePageUtility.GetSelectedPageAsync(page, TestContext.Current.CancellationToken)) Assert.Equal(Path.Combine(empty, "C", "001.png"), cover.Entry!.SystemPath);
        Assert.All(factory.Sources.Skip(1), source => Assert.True(source.IsDisposed));
    }
    /// <summary>目录封面的原生解码晚到时，取消后先释放像素及封面来源，不能进入新显示缓存。</summary>
    [Fact]
    public async Task CancelledLateCoverDecodeReleasesItsOwnSourceAndPixels()
    {
        using var fixture = new Fixture(); var library = Library(fixture); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var factory = new TrackingFactory(); await using var operation = new BookOperation(factory, new MagickImageDecoder(), state);
        await operation.OpenAsync(library, TestContext.Current.CancellationToken); var page = operation.Book!.Pages.Single(p => p.EntryName == "child");
        var decoder = new LateDecoder(); using var cache = new BitmapFactory(decoder); using var cancellation = new CancellationTokenSource();
        var load = cache.GetAsync(page, new(8, 8), cancellation.Token);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        decoder.Release.TrySetResult();
        for (int i = 0; i < 100 && factory.Sources.Skip(1).Any(s => !s.IsDisposed); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.Sources.Count); Assert.True(factory.Sources[1].IsDisposed); Assert.False(factory.Sources[0].IsDisposed);
        Assert.Empty(decoder.Image!.Pixels); Assert.Equal(0, cache.ByteCount);
    }
    /// <summary>空书及未知文件不送解码器；逐页失败不终止同帧和可见胶片条，菜单跟随当前页。</summary>
    [AvaloniaFact]
    public async Task FormalViewerRendersEmptyCardsAndKeepsOtherPagesLoading()
    {
        using var fixture = new Fixture(); var library = Path.Combine(fixture.Root, "cards"); Directory.CreateDirectory(Path.Combine(library, "empty"));
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(library, "001.png")); await File.WriteAllTextAsync(Path.Combine(library, "note.txt"), "unsupported", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); Config.Current.System.BookPageCollectMode = BookPageCollectMode.All;
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage; Config.Current.FilmStrip.IsEnabled = true;
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state), images, new NoPlatform()); window.Show();
        try
        {
            Assert.False(window.IsCommandAvailable("MoveToChildBook")); await window.OpenAsync(library); window.UpdateLayout(); await window.Viewer.RefreshAsync();
            Assert.Equal(2, operation.Frame!.Elements.Count);
            for (int i = 0; i < 100 && window.Viewer.DisplayCount == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            var empty = operation.Book!.Pages.Single(p => p.EntryName == "empty"); Assert.Equal("", window.Viewer.GetPageError(empty)); Assert.Equal(1, window.Viewer.DisplayCount);
            Assert.True(window.IsCommandAvailable("MoveToChildBook")); Assert.True(window.IsCommandAvailable("MoveToParentBook"));
            var strip = window.FindControl<ThumbnailView>("DockFilmStripSocket")!; await strip.RefreshAsync(); Assert.Equal(1, strip.DisplayCount);
            Assert.Equal("", strip.GetPageError(empty)); Assert.Contains("尚未迁移", strip.GetPageError(operation.Book.Pages.Single(p => p.EntryName == "note.txt"))!);
            var fitted = ArchivePageRenderer.Fit(new Avalonia.Rect(0, 0, 400, 400), new Avalonia.Size(800, 200)); Assert.Equal(100, fitted.Height); Assert.Equal(400, fitted.Width);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var screenshot = window.CaptureRenderedFrame(); Assert.NotNull(screenshot);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-book-hierarchy";
            screenshot!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-book-cards-layout.png")), PngBitmapEncoderOptions.Default);
            var book = operation.Book; var rect = window.Viewer.GetContentRect();
            var point = window.Viewer.TranslatePoint(new Point(rect.Right - operation.Frame!.Elements[0].Width * operation.Frame.Scale / 2, rect.Y + rect.Height / 3), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Same(book, operation.Book); Assert.Same(empty, operation.Book!.CurrentPage);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            for (int i = 0; i < 100 && ReferenceEquals(book, operation.Book); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.Equal(Path.Combine(library, "empty"), operation.Book!.Path); Assert.False(window.IsCommandAvailable("MoveToChildBook"));
            await window.ExecuteAsync("MoveToParentBook"); await window.ExecuteAsync("ToggleIsRecursiveFolder"); Assert.True(operation.Book!.Setting.IsRecursiveFolder);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>真实归档封面保留横图比例；空ZIP卡片可双击进入空书并返回，无静默简化。</summary>
    [AvaloniaFact]
    public async Task FormalArchiveCardsDecodeLandscapeCoverAndOpenEmptyZip()
    {
        using var fixture = new Fixture(); var library = Path.Combine(fixture.Root, "archive-cards"); Directory.CreateDirectory(library);
        var wideFile = Path.Combine(fixture.Root, "wide.png"); using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 800, 200)) image.Write(wideFile);
        var zip = Path.Combine(library, "book.cbz"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(wideFile, "wide.png");
        var emptyZip = Path.Combine(library, "empty.cbz"); using (ZipFile.Open(emptyZip, ZipArchiveMode.Create)) { }
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow(); window.Bind(new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state), images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(library); window.UpdateLayout(); await window.Viewer.RefreshAsync();
            for (int i = 0; i < 100 && window.Viewer.DisplayCount == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.Equal(PageType.Archive, operation.Book!.CurrentPage!.PageType); Assert.Equal(1, window.Viewer.DisplayCount);
            using (var cover = await images.GetAsync(operation.Book.CurrentPage, new(480, 640), TestContext.Current.CancellationToken)) Assert.Equal(new NeeView.Size(480, 120), cover.Image.Size);
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-book-hierarchy";
            using var screenshot = window.CaptureRenderedFrame(); screenshot!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-archive-cover-layout.png")), PngBitmapEncoderOptions.Default);
            await operation.JumpAsync(operation.Book.Pages.Single(p => p.EntryName == "empty.cbz").Index); await window.Viewer.RefreshAsync();
            Assert.Equal("", window.Viewer.GetPageError(operation.Book.CurrentPage!));
            var bounds = window.Viewer.GetContentRect(); var click = window.Viewer.TranslatePoint(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 3), window)!.Value;
            window.MouseMove(click); window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left); Assert.Equal(library, operation.Book.Path);
            window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
            for (int i = 0; i < 100 && operation.Book.Path == library; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.Equal(emptyZip, operation.Book.Path); Assert.Empty(operation.Book.Pages);
            await operation.MoveToParentBookAsync(); Assert.Equal("empty.cbz", operation.Book!.CurrentPage!.EntryName);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>创建混合普通书籍，不把测试状态目录放入阅读库。</summary>
    private static string Library(Fixture fixture)
    {
        var library = Path.Combine(fixture.Root, "library"); Directory.CreateDirectory(Path.Combine(library, "child")); Directory.CreateDirectory(Path.Combine(library, "empty"));
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(library, "001.png")); File.Copy(Path.Combine(fixture.Images, "002.png"), Path.Combine(library, "child", "002.png"));
        File.Copy(fixture.Zip, Path.Combine(library, "book.cbz")); File.WriteAllText(Path.Combine(library, "bad.cbz"), "bad archive"); File.WriteAllText(Path.Combine(library, "note.txt"), "unknown"); return library;
    }
    /// <summary>ZIP含隐式章节、同前缀目录及显式空目录，不写入用户来源。</summary>
    private static string Chapters(Fixture fixture)
    {
        var path = Path.Combine(fixture.Root, "chapters.cbz"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in new[] { "001.png", "chapter/002.png", "chapter/deep/003.png", "chapter-long/004.png" }) zip.CreateEntryFromFile(Path.Combine(fixture.Images, "001.png"), name);
        zip.CreateEntry("empty/"); return path;
    }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    /// <summary>保留真实来源并记录所有权，不模拟读取成功。</summary>
    private sealed class TrackingFactory : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public List<Archive> Sources { get; } = [];
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        { var source = await _inner.OpenAsync(path, token); Sources.Add(source); return source; }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
    }
    /// <summary>明确模拟不可即时中断的解码，只用于释放/晚到测试。</summary>
    private sealed class LateDecoder : IImageDecoder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecodedImageLease? Image { get; private set; }
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => throw new NotSupportedException();
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        { Started.TrySetResult(); await Release.Task; return Image = new(new(8, 8), new byte[256]); }
    }
}
