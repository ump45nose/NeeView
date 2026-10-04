using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原快速访问格式、嵌套重排与可靠失败回滚。</summary>
public sealed class QuickAccessTests
{
    [AvaloniaFact]
    public async Task OfficialTreeRendersAndDragsOriginalQuickAccessNodes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = await state.EditQuickAccessAsync(c => c.AddFolder(c.Root, "漫画"), TestContext.Current.CancellationToken);
        var link = await state.EditQuickAccessAsync(c => c.Add(c.Root, fixture.Images, "图片"), TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); using var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var window = new MainWindow(); window.Bind(new(operation, new(operation), state), images, new TestPlatform()); window.Show();
        try
        {
            Config.Current.Bookshelf.FolderTreeAreaHeight = 240;
            await window.SetFolderTreeVisibleAsync(true); await window.SetFolderTreeLayoutAsync(FolderTreeLayout.Top);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var tree = window.FindControl<FolderTreeView>("BookshelfDirectoryTree")!;
            var rows = tree.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
            var source = rows.Single(row => row.DataContext is QuickAccessDirectoryNode q && ReferenceEquals(q.Value, link));
            var target = rows.Single(row => row.DataContext is QuickAccessDirectoryNode q && ReferenceEquals(q.Value, folder));
            var start = source.TranslatePoint(new(65, 13), window)!.Value; var end = target.TranslatePoint(new(65, 13), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); window.MouseUp(end, MouseButton.Left);
            await PageListThumbnailTests.SettleAsync(window);
            Assert.Same(link, Assert.Single(folder.Children!));
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            var phase = System.Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p3-completion";
            frame.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-quickaccess-layout.png")), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private sealed class TestPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    [Fact]
    public async Task OriginalJsonNamesUnknownFieldsAndOrderSurviveSave()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, QuickAccessCollection.FileName);
        await File.WriteAllTextAsync(path, """{"Format":"NeeView.QuickAccess/46.3.0","Future":42,"Items":[{"Name":"资料","Children":[{"Path":"/first","More":true},{"Name":"别名","Path":"/second"}]}]}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = state.QuickAccess.Root.Children![0]; var first = folder.Children![0];
        await state.EditQuickAccessAsync(c => { c.Move(first, folder.Children[1], 1); c.Rename(first, "新/名"); return first; }, TestContext.Current.CancellationToken);
        var next = new SaveData(fixture.State); await next.LoadAsync(TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        Assert.Equal(42, raw["Future"]!.GetValue<int>());
        Assert.Equal("/second", next.QuickAccess.Root.Children![0].Children![0].Path);
        Assert.Equal("新_名", next.QuickAccess.Root.Children[0].Children![1].Name);
        Assert.True(raw["Items"]![0]!["Children"]![1]!["More"]!.GetValue<bool>());
    }
    [Fact]
    public async Task FailedSaveRestoresSameNodesPathsAndTreeOrder()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = await state.EditQuickAccessAsync(c => c.AddFolder(c.Root, "目录"), TestContext.Current.CancellationToken);
        var node = await state.EditQuickAccessAsync(c => c.Add(folder, fixture.Images), TestContext.Current.CancellationToken);
        var previous = await File.ReadAllBytesAsync(Path.Combine(fixture.State, QuickAccessCollection.FileName), TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(fixture.State, "UserSetting.json.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.EditQuickAccessAsync(c => { c.Move(node, c.Root, 0); node.Path = "/changed"; c.Rename(folder, "changed"); return node; }, TestContext.Current.CancellationToken));
        Assert.Same(node, Assert.Single(folder.Children!)); Assert.Equal(fixture.Images, node.Path); Assert.Equal("目录", folder.Name);
        Assert.Equal(previous, await File.ReadAllBytesAsync(Path.Combine(fixture.State, QuickAccessCollection.FileName), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.EditQuickAccessAsync(c => c.AddFolder(c.Root, "未提交目录"), TestContext.Current.CancellationToken));
        Assert.Same(folder, Assert.Single(state.QuickAccess.Root.Children!));
    }
    [Fact]
    public async Task TreeAndVirtualBookshelfShareNodesAndKeepPlaceAcrossRename()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var operation = fixture.Operation(state);
        var folder = await state.EditQuickAccessAsync(c => c.AddFolder(c.Root, "收藏"), TestContext.Current.CancellationToken);
        var item = await state.EditQuickAccessAsync(c => c.Add(folder, fixture.Zip), TestContext.Current.CancellationToken);
        var shelf = operation.Bookshelf; Assert.True(await shelf.SetPlaceAsync(state.QuickAccess.GetPath(folder), token: TestContext.Current.CancellationToken));
        Assert.Same(item, Assert.Single(shelf.Items).QuickAccess);
        var tree = shelf.FolderTree; var root = tree.QuickAccessRoot!;
        tree.SelectedItem = Assert.IsType<QuickAccessDirectoryNode>(Assert.Single(root.Children));
        await state.EditQuickAccessAsync(c => { c.Rename(folder, "漫画"); return folder; }, TestContext.Current.CancellationToken);
        Assert.Equal("quickaccess:/漫画", shelf.Place); Assert.Same(folder, ((QuickAccessDirectoryNode)tree.SelectedItem!).Value);
        await state.EditQuickAccessAsync(c => { c.Remove(folder); return folder; }, TestContext.Current.CancellationToken);
        Assert.Equal("quickaccess:", shelf.Place); Assert.Empty(shelf.Items);
    }
    [Fact]
    public void FoldersUseOriginalDuplicateNamesAndRejectCycles()
    {
        var c = new QuickAccessCollection(); var a = c.AddFolder(c.Root, "目录"); var b = c.AddFolder(c.Root, "目录");
        Assert.Equal("目录 (2)", b.Name); var child = c.AddFolder(a, "子目录");
        Assert.Throws<InvalidOperationException>(() => c.Move(a, child, 0));
        Assert.Same(a, c.ParentOf(child)); Assert.Same(child, c.FindNode("quickaccess:/目录/子目录"));
    }
}
