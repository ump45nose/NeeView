using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原登记阈值、可靠清理及历史文件关闭事务，使用独立临时来源和正式视图。</summary>
public sealed class HistoryPolicyTests
{
    [Fact]
    public async Task ThresholdTracksPrimaryPagesAndExistingBookUsesOneChange()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        Config.Current.History.HistoryEntryPageCount = 2;
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.SaveAsync(); Assert.Empty(state.HistoryEntries);
        await op.JumpAsync(0); op.SetViewport(new(800, 600), 1); await op.SaveAsync(); Assert.Empty(state.HistoryEntries);
        await op.MoveAsync(1); await op.SaveAsync(); Assert.Empty(state.HistoryEntries);
        await op.MoveAsync(1); await op.SaveAsync(); Assert.Single(state.HistoryEntries);
        await op.OpenAsync(f.Zip, token); await op.OpenAsync(f.Images, token); Assert.False(op.Book!.IsNew);
        var before = state.HistoryEntries.Single().LastAccessTime; await op.SaveAsync(); Assert.Equal(before, state.HistoryEntries.Single().LastAccessTime);
        await op.MoveAsync(1); await op.SaveAsync(); Assert.True(state.HistoryEntries.Single().LastAccessTime > before);
    }
    [Fact]
    public async Task SamePrimaryPageAndSplitDoNotMeetThreshold()
    {
        using var f = new Fixture(); using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Blue, 900, 300)) image.Write(Path.Combine(f.Images, "001.png"));
        var state = new SaveData(f.State); var token = TestContext.Current.CancellationToken; await state.LoadAsync(token);
        Config.Current.History.HistoryEntryPageCount = 1; Config.Current.BookSettingDefault.IsSupportedDividePage = true;
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); var first = op.Book!.CurrentPage;
        await op.MoveAsync(1); Assert.Same(first, op.Book.CurrentPage); await op.SaveAsync(); Assert.Empty(state.HistoryEntries);
        await op.MoveAsync(1); await op.SaveAsync(); Assert.Single(state.HistoryEntries);
    }
    [Fact]
    public async Task SwitchDoesNotBypassThresholdButSettingsAndExitDo()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        Config.Current.History.HistoryEntryPageCount = 99; var op = f.Operation(state);
        await op.OpenAsync(f.Images, token); await op.OpenAsync(f.Zip, token); Assert.Empty(state.HistoryEntries);
        await op.ApplySettingAsync(s => s.BookReadOrder = PageReadOrder.LeftToRight); await op.SaveAsync(); Assert.Equal(f.Zip, Assert.Single(state.HistoryEntries).Path);
        await op.OpenAsync(f.Images, token); await op.DisposeAsync(); Assert.Equal(2, state.HistoryEntries.Count);
    }
    [Fact]
    public async Task ForcedHistoryReplayUpdatesDateOnlyAfterEligibility()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.SaveAsync();
        await op.OpenAsync(f.Zip, token); var initial = state.HistoryEntries.Single(e => e.Path == f.Images).LastAccessTime; await op.OpenHistoryAsync(f.Images); await op.MoveAsync(1); await op.SaveAsync(); Assert.Equal(initial, state.HistoryEntries.Single(e => e.Path == f.Images).LastAccessTime);
        Config.Current.History.IsForceUpdateHistory = true; await op.SaveAsync(); Assert.Equal(f.Images, state.HistoryEntries[0].Path); Assert.True(state.HistoryEntries[0].LastAccessTime > initial);
    }
    [Fact]
    public async Task DisabledHistoryDeletesOnlyHistoryFileAndKeepsRuntimeAndLastBook()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.JumpAsync(2); await op.SaveAsync();
        await op.ApplyOptionsAsync(() => Config.Current.History.IsSaveHistory = false, (1, TimeSpan.Zero));
        Assert.False(File.Exists(Path.Combine(f.State, "History.json"))); Assert.Single(state.HistoryEntries);
        await state.AddBookmarkFolderAsync(null, "仍可保存", token); Assert.False(File.Exists(Path.Combine(f.State, "History.json")));
        await op.DisposeAsync(); var fresh = new SaveData(f.State); await fresh.LoadAsync(token); Assert.False(Config.Current.History.IsSaveHistory);
        Assert.Empty(fresh.HistoryEntries); Assert.Equal("003.png", fresh.GetLastBook()!.Page); Assert.Single(fresh.BookmarkRoot.Children!);
        await using var restored = f.Operation(fresh); await restored.RestoreLastAsync(token); Assert.Equal(2, restored.Position.Index);
    }
    [Fact]
    public async Task DisabledHistoryFailureAndInterruptedDeletionRestoreOriginalFile()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.SaveAsync();
        var history = Path.Combine(f.State, "History.json"); var before = await File.ReadAllTextAsync(history, token);
        var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
        try { await Assert.ThrowsAnyAsync<Exception>(() => op.ApplyOptionsAsync(() => Config.Current.History.IsSaveHistory = false, (-1, TimeSpan.Zero))); }
        finally { Directory.Delete(blocker); }
        Assert.True(Config.Current.History.IsSaveHistory); Assert.Equal(before, await File.ReadAllTextAsync(history, token));
        var marker = new JsonObject(); foreach (var name in new[] { "History.json", "UserSetting.json", "Bookmark.json", "Foldres.json" })
        { var path = Path.Combine(f.State, name); marker[name] = File.Exists(path); if (File.Exists(path)) File.Copy(path, path + ".save-backup"); }
        await File.WriteAllTextAsync(Path.Combine(f.State, ".save-pending.json"), marker.ToJsonString(), token); File.Delete(history);
        var restarted = new SaveData(f.State); await restarted.LoadAsync(token); Assert.Equal(before, await File.ReadAllTextAsync(history, token)); Assert.Single(restarted.HistoryEntries);
    }
    [Theory]
    [InlineData("permission")]
    [InlineData("timeout")]
    [InlineData("cancel")]
    public async Task CleanupChecksWholeBatchBeforeAnyRemoval(string failure)
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; await SeedAsync(f, "/first", "/second"); var state = new SaveData(f.State); await state.LoadAsync(token);
        var before = await File.ReadAllTextAsync(Path.Combine(f.State, "History.json"), token); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<bool> Check(string path, CancellationToken ct)
        { if (path == "/first") return Task.FromResult(false); if (failure == "cancel") { cancel.Cancel(); ct.ThrowIfCancellationRequested(); } if (failure == "permission") throw new UnauthorizedAccessException(); throw new TimeoutException(); }
        await Assert.ThrowsAnyAsync<Exception>(() => state.RemoveUnlinkedHistoryAsync(Check, cancel.Token)); Assert.Equal(2, state.HistoryEntries.Count);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(f.State, "History.json"), token));
        Assert.Equal(2, await state.RemoveUnlinkedHistoryAsync((_, _) => Task.FromResult(false), token)); Assert.Empty(state.HistoryEntries);
    }
    [Fact]
    public async Task LateCleanupDoesNotDeleteNewVisitOrChangedReadingState()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.SaveAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = state.RemoveUnlinkedHistoryAsync((_, _) => { started.TrySetResult(); return result.Task; }, token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3), token); await op.MoveAsync(1); await op.SaveAsync(); result.SetResult(false);
        Assert.Equal(0, await cleanup); Assert.Single(state.HistoryEntries);
    }
    [Fact]
    public async Task ExistsChecksArchiveDirectoriesAndPreservesOfflineOrCorruptPaths()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var factory = new ArchiveFactory();
        Assert.True(await factory.ExistsAsync(f.Images, token)); Assert.True(await factory.ExistsAsync(Path.Combine(f.Zip, "001.png"), token));
        Assert.False(await factory.ExistsAsync(Path.Combine(f.Zip, "missing.png"), token)); Assert.False(await factory.ExistsAsync(Path.Combine(f.Root, "missing"), token));
        await Assert.ThrowsAsync<IOException>(() => factory.ExistsAsync("/Volumes/neeview-nonexistent-" + Guid.NewGuid().ToString("N") + "/book.cbz", token));
        await Assert.ThrowsAsync<NotSupportedException>(() => factory.ExistsAsync(@"C:\\book.cbz", token));
        var nested = Path.Combine(f.Root, "nested.cbz"); using (var zip = System.IO.Compression.ZipFile.Open(nested, System.IO.Compression.ZipArchiveMode.Create))
        { zip.CreateEntry("chapter/001.png"); zip.CreateEntry("inner.cbz"); }
        Assert.True(await factory.ExistsAsync(Path.Combine(nested, "chapter"), token));
        var corrupt = await Assert.ThrowsAnyAsync<Exception>(() => factory.ExistsAsync(Path.Combine(nested, "inner.cbz", "001.png"), token));
        Assert.IsNotType<FileNotFoundException>(corrupt); // 已支持嵌套；损坏读取不能误报为缺失并清理历史。
    }
    [Fact]
    public async Task ClearInPlaceUsesCurrentTargetsIncludingAliasesWithoutPrefixDeletion()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; var state = new SaveData(f.State); await state.LoadAsync(token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, token); await op.SaveAsync(); await op.OpenAsync(f.Zip, token); await op.SaveAsync();
        var a = await state.AddBookmarkFolderAsync(null, "A", token); a.Children!.Add(new() { Path = f.Zip }); a.Children.Add(new() { Path = f.Zip });
        await op.Bookshelf.SetPlaceAsync("bookmark:A", token: token); Assert.Equal(1, await op.ClearHistoryInPlaceAsync(token)); Assert.Equal(f.Images, Assert.Single(state.HistoryEntries).Path);
    }
    [AvaloniaFact]
    public async Task StartupCleanupAndHistoryPolicyDraftUseFormalViewAndSameTransaction()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken; await SeedAsync(f, f.Zip, Path.Combine(f.Root, "missing.cbz"));
        var state = new SaveData(f.State); await state.LoadAsync(token); Config.Current.History.IsAutoCleanupEnabled = true;
        var op = f.Operation(state); var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, new BitmapFactory(new MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            await WaitAsync(() => state.HistoryEntries.Count == 1); Assert.Equal(f.Zip, state.HistoryEntries[0].Path);
            var settings = new SettingsWindow(model); settings.Show(window); settings.SelectHistoryPage(); Pump(settings);
            settings.FindControl<CheckBox>("SaveHistory")!.IsChecked = false; settings.FindControl<CheckBox>("ForceUpdateHistory")!.IsChecked = true;
            settings.FindControl<NumericUpDown>("HistoryEntryPageCount")!.Value = 7;
            var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
                Assert.True(Config.Current.History.IsSaveHistory); Assert.False(Config.Current.History.IsForceUpdateHistory); Assert.Equal(0, Config.Current.History.HistoryEntryPageCount);
                Assert.False(settings.FindControl<CheckBox>("SaveHistory")!.IsChecked);
            }
            finally { Directory.Delete(blocker); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.False(Config.Current.History.IsSaveHistory); Assert.True(Config.Current.History.IsForceUpdateHistory); Assert.Equal(7, Config.Current.History.HistoryEntryPageCount);
            Assert.False(File.Exists(Path.Combine(f.State, "History.json"))); Assert.True(window.IsCommandAvailable("ClearHistoryInPlace"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task SeedAsync(Fixture f, params string[] paths)
    {
        Directory.CreateDirectory(f.State); await File.WriteAllTextAsync(Path.Combine(f.State, "History.json"), new JsonObject
        { ["Items"] = new JsonArray(paths.Select(path => (JsonNode)new JsonObject { ["Path"] = path, ["Page"] = "001.png", ["LastAccessTime"] = DateTime.Now }).ToArray()) }.ToJsonString(), TestContext.Current.CancellationToken);
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitAsync(Func<bool> done) { for (int i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(done()); }
    private sealed class NoPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token) => throw new NotSupportedException(); }
}
