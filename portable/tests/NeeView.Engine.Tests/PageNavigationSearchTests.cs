using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
/// <summary>原来源/正文双集合、目录代表页、表达式和正式搜索输入的集成回归。</summary>
public sealed class PageNavigationSearchTests
{
    [Fact]
    public async Task SearchSortClearAndEmptyKeepSameSourcePagesAndReadingEntry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); var book = op.Book!; var source = book.Pages.SourcePages.ToArray(); await op.JumpAsync(2); var selected = book.CurrentPage;
        Assert.True(await op.SearchPagesAsync("003 /or 005", book, TestContext.Current.CancellationToken)); Assert.Equal(2, book.Pages.Count); Assert.Same(selected, book.CurrentPage);
        await op.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending); Assert.Equal(new[] { "005.png", "003.png" }, book.Pages.Select(p => p.EntryName));
        Assert.True(await op.SearchPagesAsync("impossible", book, TestContext.Current.CancellationToken)); Assert.Empty(book.Pages); Assert.Null(op.Frame); Assert.Same(selected, book.CurrentPage);
        await op.SaveAsync(); Assert.Equal("003.png", state.GetLastBook()!.Page);
        await op.SearchPagesAsync("", book, TestContext.Current.CancellationToken); Assert.Equal(5, book.Pages.Count); Assert.Same(selected, book.CurrentPage); Assert.Equal(source, book.Pages.SourcePages);
        await op.SearchPagesAsync("/rating /gt 3", book, TestContext.Current.CancellationToken); Assert.Empty(book.Pages);
        await op.SearchPagesAsync("", book, TestContext.Current.CancellationToken); Assert.Equal(5, book.Pages.Count);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => op.SearchPagesAsync("001", book, cancelled.Token)); Assert.Equal(5, book.Pages.Count);
    }
    [Fact]
    public async Task PlaylistFilterUsesAllSourceMarkersButNavigationUsesVisibleIndices()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); await op.Playlists.InitializeAsync(TestContext.Current.CancellationToken); await op.JumpAsync(1); await op.TogglePlaylistItemAsync(); await op.JumpAsync(3); await op.TogglePlaylistItemAsync();
        var book = op.Book!; await op.SearchPagesAsync("/playlist", book, TestContext.Current.CancellationToken); Assert.Equal(new[] { "002.png", "004.png" }, book.Pages.Select(p => p.EntryName));
        await op.SearchPagesAsync("002", book, TestContext.Current.CancellationToken); Assert.Equal(2, book.Marker.Markers.Count); Assert.Single(book.Pages); Assert.Null(book.Marker.GetNearMarkedPage(0, 1, false, false));
        await op.SearchPagesAsync("/size /gt 1", book, TestContext.Current.CancellationToken); Assert.Equal(5, book.Pages.Count);
    }
    [Fact]
    public async Task OriginalSmartPrefixAndSourceDirectoryTreeIgnoreSearchAndReverseSort()
    {
        using var f = new Fixture(); var zipPath = Path.Combine(f.Root, "nested.cbz");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) foreach (var name in new[] { "book/A/002.png", "book/A/deep/003.png", "book/B/001.png" }) zip.CreateEntryFromFile(Path.Combine(f.Images, "001.png"), name);
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state); Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.IncludeSubArchives;
        await op.OpenAsync(zipPath, TestContext.Current.CancellationToken); var book = op.Book!; Assert.Equal(3, book.Pages.Count); Assert.All(book.Pages, p => Assert.Equal("book/", p.Prefix));
        Assert.Equal("A > 002.png", book.Pages[0].GetDisplayName(PageNameFormat.Smart)); Assert.Equal("002.png", book.Pages[0].GetDisplayName(PageNameFormat.NameOnly));
        await op.ApplySettingAsync(s => s.SortMode = PageSortMode.FileNameDescending); await op.SearchPagesAsync("001", book, TestContext.Current.CancellationToken);
        var tree = BookTableOfContents.Create(book.Pages.SourcePages, TestContext.Current.CancellationToken); var top = Assert.Single(tree.Children);
        Assert.Equal("book", top.Name); Assert.Equal("book/A/002.png", top.Page!.EntryName); Assert.Equal(new[] { "A", "B" }, top.Children.Select(n => n.Name));
        Assert.Equal("book/A/deep/003.png", Assert.Single(top.Children[0].Children).Page!.EntryName); Assert.Single(book.Pages); Assert.Equal(3, book.Pages.SourcePages.Count);
    }
    [AvaloniaFact]
    public async Task OfficialPageSearchFocusTreeAndSettingsAreIndependentFromReader()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var op = f.Operation(state); await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        using var images = new BitmapFactory(new MagickImageDecoder()); var model = new NeeView.MacOS.ViewModels.ReaderWorkspaceViewModel(op, new(op), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            await window.ExecuteAsync("FocusPageListSearchBox"); var input = window.FindControl<TextBox>("PageListSearchBox")!; Assert.True(input.IsFocused);
            Config.Current.System.IsIncrementalSearchEnabled = false; input.Text = "002 /or 004"; Assert.True(await model.PageSearch.SearchAsync()); await PageListThumbnailTests.SettleAsync(window);
            Assert.Equal(2, op.Book!.Pages.Count); Assert.Equal("002 /or 004", Assert.Single(state.PageListSearchHistory));
            await window.ChangePageNavigationAsync(c => { c.IsFolderTreeVisible = true; c.FolderTreeLayout = FolderTreeLayout.Top; c.FolderTreeAreaHeight = 100; c.Format = PageNameFormat.NameOnly; c.IsGroupBy = true; });
            Assert.True(window.FindControl<TreeView>("ContentsTree")!.IsVisible); Assert.Equal(5, op.Book.Pages.SourcePages.Count);
            await window.ExecuteAsync("FocusFolderSearchBox"); Assert.True(window.FindControl<TextBox>("FolderSearchBox")!.IsFocused); await window.ExecuteAsync("FocusMainView"); Assert.True(window.Viewer.IsFocused);
            model.ShowPanel("PageListPanel"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            var phase = System.Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-completion";
            frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-page-search-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            await op.SaveAllAsync(TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp")); await window.ChangePageNavigationAsync(c => c.Format = PageNameFormat.PageNumber); Assert.Equal(PageNameFormat.NameOnly, Config.Current.PageList.Format); Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private sealed class TestPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
}
