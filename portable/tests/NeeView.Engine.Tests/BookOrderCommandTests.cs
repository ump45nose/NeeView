using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原来源资格、四个排序命令及唯一JSON保存；全部使用隔离夹具。</summary>
public sealed class BookOrderCommandTests
{
    [Theory]
    [InlineData("SetBookOrderByPathA", FolderOrder.Path)]
    [InlineData("SetBookOrderByPathD", FolderOrder.PathDescending)]
    [InlineData("SetBookOrderByEntryTimeA", FolderOrder.EntryTime)]
    [InlineData("SetBookOrderByEntryTimeD", FolderOrder.EntryTimeDescending)]
    public async Task BookmarkCommandsUseTargetPathAndTreeIndexNotDate(string command, FolderOrder order)
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); Config.Current.Bookshelf.FolderSortOrder = FolderSortOrder.None;
        var x = new BookmarkNode { Name = "a", Path = "/books/20.cbz", EntryTime = DateTime.MaxValue };
        var y = new BookmarkNode { Name = "z", Path = "/books/3.cbz", EntryTime = DateTime.MinValue };
        state.BookmarkRoot.Children!.Add(x); state.BookmarkRoot.Children.Add(y);
        await using var op = f.Operation(state); Assert.True(await op.Bookshelf.SetPlaceAsync("bookmark:", token: token));
        op.Bookshelf.Select(op.Bookshelf.Items.Single(i => ReferenceEquals(i.Bookmark, x)));
        await new CommandTable(op).ExecuteAsync(command);
        var expected = order is FolderOrder.Path or FolderOrder.EntryTimeDescending ? new[] { y, x } : new[] { x, y };
        Assert.Equal(expected, op.Bookshelf.Items.Select(i => i.Bookmark)); Assert.Same(x, op.Bookshelf.SelectedItem!.Bookmark);
        Assert.Equal(order, op.Bookshelf.FolderOrder); Assert.Equal(FolderOrder.FileName, Config.Current.Bookmark.BookmarkFolderOrder);
        var restored = new SaveData(f.State); await restored.LoadAsync(token);
        using var shelf = new BookshelfFolderList(new ArchiveFactory(), restored.FolderConfigs, restored);
        Assert.True(await shelf.SetPlaceAsync("bookmark:", token: token)); Assert.Equal(order, shelf.FolderOrder);
        Assert.Equal(expected.Select(i => i.Path), shelf.Items.Select(i => i.Path));
    }

    [Theory]
    [InlineData("SetBookOrderByPathA", false)]
    [InlineData("SetBookOrderByPathD", true)]
    public async Task SearchUsesFullNaturalPathAndRestoresItsParameterAfterClearing(string command, bool descending)
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await using var op = f.Operation(state);
        Config.Current.Bookshelf.FolderSortOrder = FolderSortOrder.None;
        var a = Path.Combine(f.Root, "a", "book20.cbz"); var z = Path.Combine(f.Root, "z", "book3.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(z)!); File.Copy(f.Zip, a); File.Copy(f.Zip, z);
        await op.OpenAsync(f.Zip, token); var book = op.Book;
        Assert.True(await op.Bookshelf.SetPlaceAsync(f.Root, token: token)); Assert.True(await op.Bookshelf.SearchAsync("book", f.Root, token));
        op.Bookshelf.Select(op.Bookshelf.Items.Single(i => i.Path == z));
        await new CommandTable(op).ExecuteAsync(command);
        Assert.Equal(descending ? new[] { z, a } : new[] { a, z }, op.Bookshelf.Items.Select(i => i.Path));
        Assert.Equal(z, op.Bookshelf.SelectedItem!.Path); Assert.Same(book, op.Book);
        var mode = descending ? FolderOrder.PathDescending : FolderOrder.Path;
        Assert.Equal(mode, state.FolderConfigs.GetFolderParameter(f.Root).FolderOrder);
        Assert.True(await op.Bookshelf.SearchAsync("", f.Root, token)); Assert.Equal(FolderOrder.FileName, op.Bookshelf.FolderOrder);
        Assert.False(op.CanChangeFolderOrder(mode)); Assert.Equal(mode, state.FolderConfigs.GetFolderParameter(f.Root).FolderOrder);
        var restored = new SaveData(f.State); await restored.LoadAsync(token);
        using var shelf = new BookshelfFolderList(new ArchiveFactory(), restored.FolderConfigs, restored);
        Assert.True(await shelf.SetPlaceAsync(f.Root, token: token)); Assert.Equal(FolderOrder.FileName, shelf.FolderOrder);
        Assert.True(await shelf.SearchAsync("book", f.Root, token)); Assert.Equal(mode, shelf.FolderOrder);
        Assert.Equal(descending ? new[] { z, a } : new[] { a, z }, shelf.Items.Select(i => i.Path));
    }

    [Fact]
    public async Task UnsupportedSourcesCannotMutateSortOrSaveParameters()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await using var op = f.Operation(state); var commands = new CommandTable(op);
        foreach (var place in new[] { f.Root, f.Zip, "quickaccess:" })
        {
            Assert.True(await op.Bookshelf.SetPlaceAsync(place, token: token)); var before = op.Bookshelf.Items.ToArray();
            foreach (var name in new[] { "SetBookOrderByPathA", "SetBookOrderByPathD", "SetBookOrderByEntryTimeA", "SetBookOrderByEntryTimeD" })
            {
                Assert.True(commands.IsAvailable(name)); Assert.False(op.CanChangeFolderOrder(CommandTable.BookOrderCommands[name]));
                await commands.ExecuteAsync(name); Assert.Equal(before, op.Bookshelf.Items); Assert.Equal(FolderOrder.FileName, op.Bookshelf.FolderOrder);
            }
        }
        Assert.False(File.Exists(Path.Combine(f.State, "Foldres.json")));
    }

    [Fact]
    public async Task FailedSearchSortSaveRestoresOrderSelectionAndSupportsRetry()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await using var op = f.Operation(state);
        await op.Bookshelf.SetPlaceAsync(f.Root, token: token); await op.Bookshelf.SearchAsync("cbz", f.Root, token);
        op.Bookshelf.Select(op.Bookshelf.Items.Single()); var selected = op.Bookshelf.SelectedItem!.Path;
        var blocker = Path.Combine(f.State, "Foldres.json.tmp"); Directory.CreateDirectory(blocker);
        try
        {
            Assert.NotNull(await Record.ExceptionAsync(() => new CommandTable(op).ExecuteAsync("SetBookOrderByPathD")));
            Assert.Equal(FolderOrder.FileName, op.Bookshelf.FolderOrder); Assert.Equal(selected, op.Bookshelf.SelectedItem!.Path);
        }
        finally { Directory.Delete(blocker); }
        await new CommandTable(op).ExecuteAsync("SetBookOrderByPathD"); Assert.Equal(FolderOrder.PathDescending, op.Bookshelf.FolderOrder);
    }

    [Fact]
    public async Task SortQueuedInStateGateCannotChangeNewBookmarkFolder()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await state.AddBookmarkFolderAsync(null, "new", token);
        await using var op = f.Operation(state); await op.Bookshelf.SetPlaceAsync("bookmark:", token: token);
        var gate = (SemaphoreSlim)typeof(SaveData).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        await gate.WaitAsync(token); Task pending;
        try
        {
            pending = op.ChangeFolderOrderAsync(FolderOrder.EntryTimeDescending); Assert.False(pending.IsCompleted);
            Assert.True(await op.Bookshelf.SetPlaceAsync("bookmark:new", token: token));
        }
        finally { gate.Release(); }
        await pending; Assert.Equal(FolderOrder.FileName, op.Bookshelf.FolderOrder);
        Assert.Null(state.FolderConfigs.GetFolderParameter("bookmark:new").FolderOrder);
    }

    [Fact]
    public void PathSortKeepsStableEqualPathsAndOriginalFolderGrouping()
    {
        var x = new FolderItem("z", "/same", false); var y = new FolderItem("a", "/same", false); var dir = new FolderItem("dir", "/z", true);
        foreach (var mode in new[] { FolderOrder.Path, FolderOrder.PathDescending })
            Assert.Equal(new[] { dir, x, y }, FolderCollection.Sort([x, y, dir], mode, FolderSortOrder.First, 0, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task MenusAndDropdownFollowCommittedSourceAndCheckFourCommands()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await using var op = f.Operation(state);
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow();
        window.Bind(model, new BitmapFactory(new MagickImageDecoder()), new NoPlatform()); window.Show();
        try
        {
            var menu = window.FindControl<Menu>("MenuBar")!;
            // 原默认主菜单未列这些排序命令；模拟原可定制菜单节点，仍由正式刷新/勾选处理。
            var path = new MenuItem { Tag = "SetBookOrderByPathA", ToggleType = MenuItemToggleType.CheckBox };
            var entry = new MenuItem { Tag = "SetBookOrderByEntryTimeD", ToggleType = MenuItemToggleType.CheckBox };
            menu.Items.Add(new MenuItem { Header = "测试排序", Items = { path, entry } });
            await op.Bookshelf.SetPlaceAsync(f.Root, token: token); Pump(); Assert.False(path.IsEnabled); Assert.False(entry.IsEnabled);
            Assert.DoesNotContain(model.FolderOrders, i => i.Mode.IsPathCategory() || i.Mode.IsEntryCategory());
            await op.Bookshelf.SearchAsync("cbz", f.Root, token); Pump(); Assert.True(path.IsEnabled); Assert.False(entry.IsEnabled);
            Assert.Contains(model.FolderOrders, i => i.Mode == FolderOrder.Path); await window.ExecuteAsync("SetBookOrderByPathA"); Pump(); Assert.True(path.IsChecked);
            await op.Bookshelf.SetPlaceAsync("bookmark:", token: token); Pump(); Assert.True(path.IsEnabled); Assert.True(entry.IsEnabled);
            await window.ExecuteAsync("SetBookOrderByEntryTimeD"); Pump(); Assert.True(entry.IsChecked); Assert.False(path.IsChecked);
            await op.Bookshelf.SetPlaceAsync("quickaccess:", token: token); Pump(); Assert.False(path.IsEnabled); Assert.False(entry.IsEnabled); Assert.Single(model.FolderOrders);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        void Pump() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask; }
}
