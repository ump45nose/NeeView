using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原书籍锁定、页尾策略和可重复使用的Unload，不以clamp代替终端动作。</summary>
public sealed class BookControlTests
{
    [Fact]
    public async Task BookLockRejectsOtherSourcesButPermitsSameBookImageAndReload()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = f.Operation(state);
        await operation.OpenAsync(f.Images, TestContext.Current.CancellationToken); operation.SetBookLock(true); var first = operation.Book;
        await operation.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.Same(first, operation.Book); Assert.True(operation.IsBookLocked);
        await operation.OpenAsync(Path.Combine(f.Images, "003.png"), TestContext.Current.CancellationToken); Assert.NotSame(first, operation.Book); Assert.Equal(2, operation.Position.Index);
        await operation.OpenAsync(f.Images, TestContext.Current.CancellationToken); Assert.Equal(f.Images, operation.Book!.Path);
        await operation.MoveBookAsync(1); Assert.Equal(f.Images, operation.Book.Path);
        await operation.UnloadAsync(TestContext.Current.CancellationToken); Assert.Null(operation.Book); Assert.False(operation.IsBookLocked); Assert.Null(state.LastBookPath);
        await operation.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.Equal(f.Zip, operation.Book!.Path); Assert.True(operation.CanUnload);
    }
    [Fact]
    public async Task UnloadCancelsPendingOpenAndDisposesLateSourceWithoutDisposingReader()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = new LateOpenFactory(f.Zip); await using var op = new BookOperation(source, new NeeView.Backends.MagickImageDecoder(), state);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var opening = op.OpenAsync(f.Zip, TestContext.Current.CancellationToken);
        await source.Started.Task.WaitAsync(TestContext.Current.CancellationToken); Assert.True(op.CanUnload);
        await op.UnloadAsync(TestContext.Current.CancellationToken); Assert.Null(op.Book); source.Release.SetResult(); await opening;
        Assert.True(source.LateSource!.IsDisposed); Assert.Null(op.Book); Assert.False(op.IsLoading); Assert.Null(state.LastBookPath);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); Assert.NotNull(op.Frame);
    }
    [Fact]
    public async Task UnloadSaveFailureRetainsCurrentSourceAndRetryClearsLastBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = f.Operation(state);
        await operation.OpenAsync(f.Images, TestContext.Current.CancellationToken); await operation.SaveAsync(); var book = operation.Book; operation.SetBookLock(true);
        var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
        try { var error = await Record.ExceptionAsync(() => operation.UnloadAsync(TestContext.Current.CancellationToken)); Assert.True(error is IOException or UnauthorizedAccessException); Assert.Same(book, operation.Book); Assert.False(operation.IsBookLocked); Assert.True(operation.CanUnload); }
        finally { Directory.Delete(blocker); }
        await operation.UnloadAsync(TestContext.Current.CancellationToken); Assert.Null(operation.Book); Assert.Null(operation.Frame); Assert.Null(operation.Context); Assert.False(operation.CanUnload);
        var restarted = new SaveData(f.State); await restarted.LoadAsync(TestContext.Current.CancellationToken); Assert.Null(restarted.LastBookPath); Assert.Single(restarted.HistoryEntries);
        await operation.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.NotNull(operation.Frame);
    }
    [Theory]
    [InlineData(PageEndAction.None, 4)]
    [InlineData(PageEndAction.Loop, 0)]
    [InlineData(PageEndAction.SeamlessLoop, 0)]
    public async Task ForwardBoundaryUsesOriginalAction(PageEndAction action, int expected)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SetPageEndActionAsync(action); await op.JumpAsync(4, true); await op.MoveAsync(1); Assert.Equal(expected, op.Position.Index);
        if (action == PageEndAction.None) Assert.Equal("已到末页。", op.Error);
        else { await op.JumpAsync(0); await op.MoveAsync(-1); Assert.Equal(4, op.Position.Index); }
    }
    [Fact]
    public async Task SizeStepTerminatesOnlyAfterReachingBoundaryAndLoopsThroughOriginalFrames()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SetPageEndActionAsync(PageEndAction.Loop); await op.MoveSizeAsync(10); Assert.Equal(4, op.Position.Index);
        await op.MoveSizeAsync(10); Assert.Equal(0, op.Position.Index);
        await op.SetPageEndActionAsync(PageEndAction.SeamlessLoop); await op.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.IsSupportedSingleFirstPage = false; s.IsSupportedSingleLastPage = false; });
        await op.JumpAsync(4); Assert.Equal(2, op.Frame!.Elements.Count); Assert.Contains(op.Frame.Elements, e => e.Page.Index == 0);
        await op.MoveAsync(1); Assert.InRange(op.Position.Index, 0, 4); Assert.DoesNotContain(op.Frame.Elements, e => e.Page.Index == 4);
    }
    [Theory]
    [InlineData(ResetNextBookPageMode.Continue, 4)]
    [InlineData(ResetNextBookPageMode.Reset, 0)]
    [InlineData(ResetNextBookPageMode.None, 2)]
    public async Task NextBookBoundaryKeepsDirectionResetPolicy(ResetNextBookPageMode policy, int expectedBackward)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        var library = Path.Combine(f.Root, "library"); var first = Path.Combine(library, "A"); Directory.CreateDirectory(first);
        foreach (var file in Directory.EnumerateFiles(f.Images)) File.Copy(file, Path.Combine(first, Path.GetFileName(file)));
        var second = Path.Combine(library, "B.cbz"); File.Copy(f.Zip, second);
        await op.OpenAsync(first, TestContext.Current.CancellationToken); await op.JumpAsync(2); await op.SaveAsync(); await op.OpenAsync(second, TestContext.Current.CancellationToken);
        await op.Bookshelf.SetPlaceAsync(library, second, TestContext.Current.CancellationToken); Config.Current.Book.ResetNextBookPageMode = policy; await op.SetPageEndActionAsync(PageEndAction.NextBook);
        await op.MoveAsync(-1); Assert.Equal(first, op.Book!.Path); Assert.Equal(expectedBackward, op.Position.Index);
        op.SetBookLock(true); await op.JumpAsync(4, true); await op.MoveAsync(1); Assert.Equal(first, op.Book.Path);
    }
    [Fact]
    public async Task DialogRejectsLateChoiceAndDoesNotReenter()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.SetPageEndActionAsync(PageEndAction.Dialog); await op.JumpAsync(4, true);
        var response = new TaskCompletionSource<PageEndAction>(TaskCreationOptions.RunContinuationsAsynchronously); int requests = 0;
        op.PageEndDialogAsync = (_, _) => { requests++; return response.Task; };
        var boundary = op.MoveAsync(1); Assert.Equal(1, requests); await op.MoveAsync(1); Assert.Equal(1, requests);
        await op.JumpAsync(2); response.SetResult(PageEndAction.Loop); await boundary; Assert.Equal(2, op.Position.Index);
        op.PageEndDialogAsync = (_, _) => Task.FromResult(PageEndAction.None); await op.JumpAsync(4, true); await op.MoveAsync(1); Assert.Equal(4, op.Position.Index); Assert.Null(op.Error);
    }
    [AvaloniaFact]
    public async Task OfficialHostLockParametersDialogAndUnloadUseUniqueReader()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); var op = f.Operation(state);
        var window = new MainWindow(); var model = new ReaderWorkspaceViewModel(op, new CommandTable(op), state); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Pump(window); state.SetCommandParameter("ToggleBookLock", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await window.ExecuteAsync("ToggleBookLock"); await window.ExecuteAsync("ToggleBookLock"); Assert.True(op.IsBookLocked); await window.ExecuteAsync("ToggleBookLock", true); Assert.False(op.IsBookLocked);
            await window.ExecuteAsync("SetPageOrientationVertical"); Assert.Equal(PageFrameOrientation.Vertical, op.Context!.FrameOrientation); await window.ExecuteAsync("TogglePageOrientation"); Assert.Equal(PageFrameOrientation.Horizontal, op.Context.FrameOrientation);
            await op.SetPageEndActionAsync(PageEndAction.Dialog); await op.JumpAsync(4, true); var action = op.MoveAsync(1); await WaitAsync(() => window.OwnedWindows.Any());
            var dialog = window.OwnedWindows.Single(); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using (var screenshot = dialog.CaptureRenderedFrame()) SaveImage(screenshot!);
            dialog.FindControl<Button>("Loop")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await action; Assert.Equal(0, op.Position.Index);
            await window.ExecuteAsync("Unload"); await window.Viewer.RefreshAsync(); Assert.Equal(0, window.Viewer.DisplayCount); Assert.Equal("", model.Address); Assert.False(window.IsCommandAvailable("Unload"));
            await window.OpenAsync(f.Zip); Pump(window); await window.Viewer.RefreshAsync(); await WaitAsync(() => window.Viewer.DisplayCount > 0);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitAsync(Func<bool> condition) { for (int i = 0; i < 200 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(condition()); }
    private static void SaveImage(Avalonia.Media.Imaging.Bitmap image) { var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-book-controls"; image.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-page-end-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    private sealed class NoPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask; }
    private sealed class LateOpenFactory(string pausedPath) : IArchiveFactory
    {
        private readonly NeeView.Backends.ArchiveFactory _inner = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Archive? LateSource { get; private set; }
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        {
            if (path != pausedPath) return await _inner.OpenAsync(path, token);
            Started.SetResult(); await Release.Task; LateSource = await _inner.OpenAsync(path, CancellationToken.None); return LateSource;
        }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
    }
}
