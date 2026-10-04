using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
public sealed class BookshelfSearchTests
{
    [Fact]
    public async Task OriginalRecursiveSearchAttributesAndSortingLeaveReaderUntouched()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state); await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        var root = Path.Combine(f.Root, "library"); var sub = Path.Combine(root, "nested"); Directory.CreateDirectory(sub); File.Copy(f.Zip, Path.Combine(root, "book2.cbz")); var target = Path.Combine(sub, "book10.cbz"); File.Copy(f.Zip, target);
        await op.OpenAsync(target, TestContext.Current.CancellationToken); await state.RegisterBookmarkAsync(op.Book!, token: TestContext.Current.CancellationToken); await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        var shelf = op.Bookshelf; Assert.True(await shelf.SetPlaceAsync(root, token: TestContext.Current.CancellationToken)); var book = op.Book;
        Assert.True(await shelf.SearchAsync("book", root, TestContext.Current.CancellationToken)); Assert.Equal(new[] { "book2.cbz", "book10.cbz" }, shelf.Items.Select(i => i.Name));
        Assert.True(await shelf.SearchAsync("/bookmark /size /gt 1", root, TestContext.Current.CancellationToken)); Assert.Equal(target, Assert.Single(shelf.Items).Path); Assert.Same(book, op.Book);
        Config.Current.Bookshelf.IsSearchIncludeSubdirectories = false; Assert.True(await shelf.RefreshAsync(TestContext.Current.CancellationToken)); Assert.Empty(shelf.Items);
        Assert.True(await shelf.SearchAsync("", root, TestContext.Current.CancellationToken)); Assert.Equal(2, shelf.Items.Count); Assert.Same(book, op.Book);
        await Assert.ThrowsAnyAsync<Exception>(() => shelf.SearchAsync("/p.unknown a", root, TestContext.Current.CancellationToken)); Assert.Equal(2, shelf.Items.Count);
    }
    [Fact]
    public async Task RealSearchWatcherUpdatesResultsAndStopsWhileHidden()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); using var shelf = new BookshelfFolderList(new ArchiveFactory(), state: state);
        var root = Path.Combine(f.Root, "library"); Directory.CreateDirectory(root); Assert.True(await shelf.SetPlaceAsync(root, token: TestContext.Current.CancellationToken));
        shelf.IsPresented = true; Assert.True(await shelf.SearchAsync("match", root, TestContext.Current.CancellationToken));
        var one = Path.Combine(root, "match1.cbz"); File.Copy(f.Zip, one); await WaitAsync(() => shelf.Items.Count == 1);
        var two = Path.Combine(root, "match2.cbz"); File.Move(one, two); await WaitAsync(() => shelf.Items.FirstOrDefault()?.Path == two);
        var committedItems = shelf.Items;
        foreach (var missing in new[] { "quickaccess:/missing", "bookmark:/missing" })
        {
            Assert.False(await shelf.SetPlaceAsync(missing, token: TestContext.Current.CancellationToken));
            Assert.Equal(root, shelf.Place); Assert.Equal("match", shelf.SearchKeyword); Assert.Same(committedItems, shelf.Items);
        }
        shelf.IsPresented = false; File.Delete(two); await Task.Delay(350, TestContext.Current.CancellationToken); Assert.Single(shelf.Items);
        shelf.IsPresented = true; await WaitAsync(() => shelf.Items.Count == 0);
    }
    [Fact]
    public async Task SearchWatchCannotReplaceInFlightNavigation()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        foreach (bool queuedBeforeNavigation in new[] { false, true })
        {
            var source = new PausedSearchFactory(Path.Combine(f.Root, "old"), Path.Combine(f.Root, "new"));
            using var shelf = new BookshelfFolderList(source, state: state);
            Assert.True(await shelf.SetPlaceAsync(source.OldPlace, token: TestContext.Current.CancellationToken));
            shelf.IsPresented = true; Assert.True(await shelf.SearchAsync("match", source.OldPlace, TestContext.Current.CancellationToken));
            if (queuedBeforeNavigation) source.Changed!.Invoke();
            var navigation = shelf.SetPlaceAsync(source.NewPlace, token: TestContext.Current.CancellationToken);
            try
            {
                await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
                if (!queuedBeforeNavigation) source.Changed!.Invoke();
                await Task.Delay(350, TestContext.Current.CancellationToken);
                Assert.False(navigation.IsCompleted); Assert.True(shelf.IsLoading); Assert.Equal(2, source.OldReads);
                source.Release.TrySetResult(); Assert.True(await navigation); Assert.Equal(source.NewPlace, shelf.Place);
                Assert.Equal("", shelf.SearchKeyword); Assert.Null(source.Changed);
            }
            finally { source.Release.TrySetResult(); await navigation; }
        }
    }
    [Fact]
    public async Task AllFourSearchHistoriesRoundTripAndFailedEditsRestoreTheirOwnCollection()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.EditBookshelfSearchHistoryAsync("/history", token: TestContext.Current.CancellationToken); await state.EditPageListSearchHistoryAsync("/playlist", token: TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json.tmp")); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.EditPageListSearchHistoryAsync("/size /gt 1", token: TestContext.Current.CancellationToken)); Assert.Equal(new[] { "/playlist" }, state.PageListSearchHistory); Directory.Delete(Path.Combine(f.State, "UserSetting.json.tmp"));
        var next = new SaveData(f.State); await next.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(state.PageListSearchHistory, next.PageListSearchHistory); Assert.Equal(state.BookshelfSearchHistory, next.BookshelfSearchHistory);
    }
    [AvaloniaFact]
    public async Task NavigationSettingsUseOriginalFieldsAndCancelWithoutApplyingDraft()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        var model = new NeeView.MacOS.ViewModels.ReaderWorkspaceViewModel(op, new(op), state); var settings = new SettingsWindow(model); settings.Show();
        try
        {
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 5; settings.UpdateLayout(); Assert.True(settings.FindControl<ScrollViewer>("NavigationSettings")!.IsVisible);
            settings.FindControl<ComboBox>("PageNameFormat")!.SelectedIndex = 1; settings.FindControl<CheckBox>("PageTreeVisible")!.IsChecked = true; Assert.Equal(PageNameFormat.Smart, Config.Current.PageList.Format);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.Equal(PageNameFormat.NameOnly, Config.Current.PageList.Format); Assert.True(Config.Current.PageList.IsFolderTreeVisible);
            var next = new SaveData(f.State); await next.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(PageNameFormat.NameOnly, Config.Current.PageList.Format);
        }
        finally { settings.Close(); }
        var cancelled = new SettingsWindow(model); cancelled.Show(); cancelled.FindControl<ComboBox>("PageNameFormat")!.SelectedIndex = 3; cancelled.Close(); Assert.Equal(PageNameFormat.NameOnly, Config.Current.PageList.Format);
    }
    private static async Task WaitAsync(Func<bool> condition)
    { for (int i = 0; i < 100 && !condition(); i++) { await Task.Delay(30, TestContext.Current.CancellationToken); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } Assert.True(condition()); }
    /// <summary>仅暂停目标目录读取，显式回报旧监视事件，确定性复现导航与刷新竞争。</summary>
    private sealed class PausedSearchFactory(string oldPlace, string newPlace) : IArchiveFactory
    {
        public string OldPlace => oldPlace;
        public string NewPlace => newPlace;
        public int OldReads;
        public Action? Changed;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Archive> OpenAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<FolderItem>>([]);
        public async Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token)
        {
            if (path == oldPlace) OldReads++;
            if (path == newPlace) { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
            return [new("match.cbz", Path.Combine(path, "match.cbz"), false, 1)];
        }
        public IDisposable? WatchBookSearch(string path, bool recursive, Action changed)
        { Changed = changed; return new WatchLease(() => Changed = null); }
        private sealed class WatchLease(Action release) : IDisposable { public void Dispose() => release(); }
    }
}
