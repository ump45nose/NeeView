using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原默认复制与全局文件权限的来源、保存失败、菜单/快捷键及阅读隔离回归。</summary>
public sealed class DefaultSettingCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-DefaultSettings-" + Guid.NewGuid().ToString("N"));
        public string State => Path.Combine(Root, "Profile");
        public string Images => Path.Combine(Root, "Images");
        public Fixture()
        {
            Directory.CreateDirectory(State); Directory.CreateDirectory(Images);
            using var image = new MagickImage(MagickColors.Teal, 20, 40);
            for (int i = 0; i < 4; i++) image.Write(Path.Combine(Images, $"00{i}.png"));
        }
        public async Task<SaveData> Load() { var state = new SaveData(State); await state.LoadAsync(Token); return state; }
        public void Dispose() => Directory.Delete(Root, true);
        public IDisposable FailWrites()
        {
            var path = Path.Combine(State, "UserSetting.json"); if (File.Exists(path)) File.Move(path, path + ".backup");
            Directory.CreateDirectory(path); return new Cleanup(() => { Directory.Delete(path); if (File.Exists(path + ".backup")) File.Move(path + ".backup", path); });
        }
    }
    private sealed class Cleanup(Action cleanup) : IDisposable { public void Dispose() => cleanup(); }
    private sealed class Sources : IArchiveFactory
    {
        private readonly ArchiveFactory _inner = new();
        public bool Fail;
        public int Opens;
        public int DelayedOpen;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Archive> OpenAsync(string path, CancellationToken token)
        {
            if (++Opens == DelayedOpen) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            if (Fail) throw new IOException("isolated source failure");
            return await _inner.OpenAsync(path, token);
        }
        public Task<IReadOnlyList<FolderItem>> ListFoldersAsync(string path, CancellationToken token) => _inner.ListFoldersAsync(path, token);
        public Task<IReadOnlyList<FolderItem>> ListBooksAsync(string path, CancellationToken token) => _inner.ListBooksAsync(path, token);
        public Task<FolderItem?> GetFileMetadataAsync(string path, CancellationToken token) => _inner.GetFileMetadataAsync(path, token);
    }
    private sealed class Platform : IPlatformService
    {
        public int TrashCalls;
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) { TrashCalls++; throw new IOException("must not trash isolated fixture"); }
    }
    private static IEnumerable<MenuItem> Items(IEnumerable<object?> items)
    { foreach (var item in items.OfType<MenuItem>()) { yield return item; foreach (var child in Items(item.Items)) yield return child; } }
    [Fact]
    public async Task EmptyBookResetCopiesDefaultFieldsInPlaceAndPersists()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        var current = Config.Current.BookSetting; var defaults = Config.Current.BookSettingDefault;
        defaults.Page = "future-entry.png"; defaults.PageMode = PageMode.WidePage; defaults.BookReadOrder = PageReadOrder.LeftToRight;
        defaults.BaseScale = .7; defaults.EffectProfileId = 13; defaults.IsSupportedDividePage = true;
        var commands = new CommandTable(operation); Assert.True(commands.IsAvailable("SetDefaultPageSetting")); await commands.ExecuteAsync("SetDefaultPageSetting");
        Assert.Same(current, Config.Current.BookSetting); Assert.Null(operation.Book); Assert.Equal(defaults.Page, current.Page);
        Assert.Equal(JsonSerializer.Serialize(defaults), JsonSerializer.Serialize(current));
        var loaded = new SaveData(f.State); await loaded.LoadAsync(Token); Assert.Equal(.7, Config.Current.BookSetting.BaseScale); Assert.Equal(13, Config.Current.BookSetting.EffectProfileId);
    }
    [Theory]
    [InlineData(PageReadOrder.LeftToRight, PageMode.SinglePage)]
    [InlineData(PageReadOrder.RightToLeft, PageMode.WidePage)]
    public async Task ResetPreservesSourceAndCurrentImageWhileApplyingReadingDefaults(PageReadOrder direction, PageMode mode)
    {
        using var f = new Fixture(); var state = await f.Load(); var source = new Sources(); await using var operation = new BookOperation(source, new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); await operation.JumpAsync(2); var book = operation.Book!; var current = book.CurrentPage;
        var setting = book.Setting; var defaults = Config.Current.BookSettingDefault;
        defaults.PageMode = mode; defaults.BookReadOrder = direction; defaults.SortMode = PageSortMode.FileNameDescending;
        defaults.AutoRotate = AutoRotateType.ForcedLeft; defaults.BaseScale = .75; defaults.EffectProfileId = 7;
        defaults.IsSupportedSingleFirstPage = true; defaults.IsSupportedSingleLastPage = true; defaults.IsSupportedWidePage = false;
        await new CommandTable(operation).ExecuteAsync("SetDefaultPageSetting");
        Assert.Equal(1, source.Opens); Assert.Same(book, operation.Book); Assert.Same(setting, book.Setting); Assert.Same(setting, Config.Current.BookSetting);
        Assert.Contains(current!, book.CurrentPages); Assert.Equal(PageSortMode.FileNameDescending, book.EffectiveSortMode);
        Assert.Equal(JsonSerializer.Serialize(defaults), JsonSerializer.Serialize(setting)); Assert.Equal(current!.Index, operation.Position.Index);
        Assert.Equal(current.EntryName, state.Find(f.Images)!.Page);
    }
    [Fact]
    public async Task RecursiveDefaultRecollectsAndPreservesImageAndFailureKeepsOldSource()
    {
        using var f = new Fixture(); var state = await f.Load(); Config.Current.System.BookPageCollectMode = BookPageCollectMode.Image;
        var source = new Sources(); await using var operation = new BookOperation(source, new MagickImageDecoder(), state);
        Directory.CreateDirectory(Path.Combine(f.Images, "nested")); File.Copy(Path.Combine(f.Images, "000.png"), Path.Combine(f.Images, "nested", "005.png"));
        await operation.OpenAsync(f.Images, Token); await operation.JumpAsync(2); var old = operation.Book; var anchor = old!.CurrentPage!.EntryName;
        Config.Current.BookSettingDefault.IsRecursiveFolder = true; Config.Current.BookSettingDefault.BaseScale = .6;
        source.Fail = true; await Assert.ThrowsAsync<IOException>(() => operation.SetDefaultPageSettingAsync());
        Assert.Same(old, operation.Book); Assert.False(old.Setting.IsRecursiveFolder); Assert.Equal(1, old.Setting.BaseScale); Assert.Equal(4, old.Pages.Count);
        source.Fail = false; await operation.SetDefaultPageSettingAsync();
        Assert.NotSame(old, operation.Book); Assert.True(operation.Book!.Setting.IsRecursiveFolder); Assert.Equal(.6, operation.Book.Setting.BaseScale);
        Assert.Equal(5, operation.Book.Pages.Count); Assert.Equal(anchor, operation.Book.CurrentPage!.EntryName);
    }
    [Fact]
    public async Task ResetWriteFailureRollsBackReadingAndAllowsRetry()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); await operation.JumpAsync(2); await operation.SaveAllAsync(Token);
        var book = operation.Book!; var setting = book.Setting; var before = JsonSerializer.Serialize(setting); var position = operation.Position; var current = book.CurrentPage;
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage; Config.Current.BookSettingDefault.SortMode = PageSortMode.FileNameDescending;
        using (f.FailWrites()) await Assert.ThrowsAnyAsync<Exception>(() => operation.SetDefaultPageSettingAsync());
        Assert.Same(setting, book.Setting); Assert.Equal(before, JsonSerializer.Serialize(setting)); Assert.Equal(position, operation.Position); Assert.Same(current, book.CurrentPage);
        await operation.SetDefaultPageSettingAsync(); Assert.Equal(PageMode.WidePage, book.Setting.PageMode); Assert.Equal(PageSortMode.FileNameDescending, book.EffectiveSortMode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultCopyOnlyActualChangesReleaseOriginalHistorySuppression(bool change)
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); var book = operation.Book!;
        book.Setting.CopyTo(Config.Current.BookSettingDefault); book.MementoControl.OnHistoryRemoved();
        if (change) Config.Current.BookSettingDefault.BaseScale = .8;
        await operation.SetDefaultPageSettingAsync();
        Assert.Equal(!change, book.MementoControl.IsHistoryRemoved);
        Assert.Equal(!change, book.MementoControl.IsPageChangeCountEnabled);
        Assert.Equal(change, book.MementoControl.CanHistory(Config.Current.History));
    }
    [Fact]
    public async Task FailedDefaultCopyRestoresRemovedHistoryEligibility()
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); var book = operation.Book!; book.MementoControl.OnHistoryRemoved();
        Config.Current.BookSettingDefault.BaseScale = .8;
        using (f.FailWrites()) await Assert.ThrowsAnyAsync<Exception>(() => operation.SetDefaultPageSettingAsync());
        Assert.True(book.MementoControl.IsHistoryRemoved); Assert.True(book.MementoControl.IsPageChangeCountEnabled);
        Assert.False(book.MementoControl.CanHistory(Config.Current.History)); Assert.Equal(1, book.Setting.BaseScale);
    }
    [Fact]
    public async Task NewOpenWinsOverPendingRecursiveDefaultRecollection()
    {
        using var f = new Fixture(); var state = await f.Load(); var sources = new Sources { DelayedOpen = 2 };
        await using var operation = new BookOperation(sources, new MagickImageDecoder(), state);
        var other = Path.Combine(f.Root, "Other"); Directory.CreateDirectory(other); File.Copy(Path.Combine(f.Images, "000.png"), Path.Combine(other, "other.png"));
        await operation.OpenAsync(f.Images, Token); Config.Current.BookSettingDefault.IsRecursiveFolder = true;
        var reset = operation.SetDefaultPageSettingAsync(); await sources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await operation.OpenAsync(other, Token); await reset;
        Assert.Equal(other, operation.Book!.Path); Assert.Equal("other.png", operation.Book.CurrentPage!.EntryName); Assert.Null(operation.Error);
    }
    [AvaloniaFact]
    public async Task PermissionMenuAndShortcutUpdateRealCapabilitiesWithoutRefreshingReader()
    {
        using var f = new Fixture(); var state = await f.Load(); var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        using var images = new BitmapFactory(new MagickImageDecoder()); var platform = new Platform(); operation.AttachFileDeletion(platform, images);
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var window = new MainWindow(); window.Bind(model, images, platform); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await window.Viewer.RefreshAsync();
            var book = operation.Book!; var page = book.CurrentPage; int refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            state.SetCommandParameter("TogglePermitFile", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            var menus = Items(window.FindControl<Menu>("MenuBar")!.Items).ToArray(); var permission = menus.Single(i => i.Tag as string == "TogglePermitFile");
            var delete = menus.Single(i => i.Tag as string == "DeleteFile");
            Assert.False(operation.CanDeleteFile); await window.ExecuteAsync("TogglePermitFile"); Assert.True(operation.CanDeleteFile); Assert.True(permission.IsChecked); Assert.True(delete.IsEnabled);
            await window.ExecuteAsync("TogglePermitFile", true); Assert.False(operation.CanDeleteFile); Assert.False(permission.IsChecked); Assert.False(delete.IsEnabled);
            await operation.DeleteFileAsync(Token); Assert.Equal(0, platform.TrashCalls);
            Assert.Same(book, operation.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(0, refreshes);
            Assert.True(window.IsCommandAvailable("SetDefaultPageSetting"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [Fact]
    public async Task PermissionFailurePreservesFlagUnknownJsonAndRetry()
    {
        using var f = new Fixture(); File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), """{"Config":{"System":{"Future":19}}}""");
        var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        using (f.FailWrites()) await Assert.ThrowsAnyAsync<Exception>(() => operation.ToggleFileWriteAccessAsync());
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); await new CommandTable(operation).ExecuteAsync("TogglePermitFile");
        Assert.True(Config.Current.System.IsFileWriteAccessEnabled);
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
        Assert.True(raw["Config"]!["System"]!["IsFileWriteAccessEnabled"]!.GetValue<bool>()); Assert.Equal(19, raw["Config"]!["System"]!["Future"]!.GetValue<int>());
        state.SetCommandParameter("TogglePermitFile", new ToggleCommandParameter { ToggleMode = ToggleMode.Off });
        await operation.ToggleFileWriteAccessAsync(); Assert.False(Config.Current.System.IsFileWriteAccessEnabled);
    }
}
