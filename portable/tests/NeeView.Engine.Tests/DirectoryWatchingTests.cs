using NeeView.Backends;
namespace NeeView.Engine.Tests;
public sealed class DirectoryWatchingTests
{
    [Fact]
    public async Task RealDirectoryCreateRenameDeleteRefreshesAndHiddenTreeReleasesWatch()
    {
        using var fixture = new Fixture(); Config.SetCurrent(new()); var archives = new ArchiveFactory();
        using var shelf = new BookshelfFolderList(archives); using var tree = new FolderTreeModel(archives, shelf, fixture.Images);
        tree.Root.IsExpanded = true; await tree.Root.Loading; Assert.Equal(1, tree.WatchCount);
        var folder = Path.Combine(fixture.Images, "新增"); Directory.CreateDirectory(folder);
        await UntilAsync(() => tree.Root.Children.Any(n => n.Name == "新增"));
        var moved = Path.Combine(fixture.Images, "改名"); Directory.Move(folder, moved);
        await UntilAsync(() => tree.Root.Children.Any(n => n.Name == "改名") && !tree.Root.Children.Any(n => n.Name == "新增"));
        var selected = tree.Root.Children.OfType<DirectoryNode>().Single(); tree.SelectedItem = selected;
        Directory.Delete(moved); await UntilAsync(() => tree.Root.Children.Count == 0);
        Assert.Same(tree.Root, tree.SelectedItem); Assert.True(selected.IsDisposed);
        tree.IsPresented = false; Assert.Equal(0, tree.WatchCount);
        Directory.CreateDirectory(folder); await Task.Delay(350, TestContext.Current.CancellationToken); Assert.Empty(tree.Root.Children);
        tree.IsPresented = true; await UntilAsync(() => tree.Root.Children.Any(n => n.Name == "新增"));
        Assert.Equal("新增", Assert.Single(tree.Root.Children).Name); tree.Dispose(); Assert.Equal(0, tree.WatchCount);
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < end) await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
