using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

public sealed class BookmarkOperationTests
{
    /// <summary>核对原名称替换、递增及同父路径去重；其他文件夹仍可注册同书。</summary>
    [Fact]
    public async Task NamesAndRegistrationFollowOriginalRules()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var folder = await state.AddBookmarkFolderAsync(null, " 漫画/一 ", token: TestContext.Current.CancellationToken);
        Assert.Equal("漫画_一", folder.Name);
        Assert.Equal("漫画_一 (2)", (await state.AddBookmarkFolderAsync(null, "漫画\\一", token: TestContext.Current.CancellationToken)).Name);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken); await operation.JumpAsync(2);
        var first = await state.RegisterBookmarkAsync(operation.Book!, folder, token: TestContext.Current.CancellationToken);
        Assert.Same(first, await state.RegisterBookmarkAsync(operation.Book!, folder, token: TestContext.Current.CancellationToken)); Assert.Single(folder.Children!);
        var root = await state.RegisterBookmarkAsync(operation.Book!, token: TestContext.Current.CancellationToken); Assert.NotSame(first, root);
        Assert.Equal("003.png", first.Page); await state.RenameBookmarkAsync(first, "命名/书籍", token: TestContext.Current.CancellationToken); Assert.Equal("命名_书籍", first.Name);
        await state.RenameBookmarkAsync(first, "", token: TestContext.Current.CancellationToken); Assert.Null(first.Name);
    }

    /// <summary>按原 Name+Path 判断重复；普通索引移动不套用 MoveToChild 的合并规则。</summary>
    [Fact]
    public async Task ChildMoveDeduplicatesOnlyEqualNameAndPath()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var source = await state.AddBookmarkFolderAsync(null, "源", token: TestContext.Current.CancellationToken); var target = await state.AddBookmarkFolderAsync(null, "目标", token: TestContext.Current.CancellationToken);
        var duplicate = new BookmarkNode { Name = "同名", Path = "/a.cbz" }; var kept = new BookmarkNode { Name = "同名", Path = "/a.cbz" };
        var alias = new BookmarkNode { Name = "别名", Path = "/a.cbz" }; var another = new BookmarkNode { Name = "同名", Path = "/b.cbz" };
        source.Children!.Add(duplicate); source.Children.Add(alias); source.Children.Add(another); target.Children!.Add(kept);
        Assert.Same(kept, await state.MoveBookmarkAsync(duplicate, target, token: TestContext.Current.CancellationToken)); Assert.DoesNotContain(duplicate, state.BookmarkRoot.Walk());
        Assert.Same(alias, await state.MoveBookmarkAsync(alias, target, token: TestContext.Current.CancellationToken)); Assert.Same(alias, target.Children[0]);
        await state.MoveBookmarkAsync(another, target, token: TestContext.Current.CancellationToken); Assert.Equal(3, target.Children.Count);
        await state.MoveBookmarkAsync(alias, target, 999, token: TestContext.Current.CancellationToken); Assert.Same(alias, target.Children[^1]);
        await state.MoveBookmarkAsync(alias, source, 0, token: TestContext.Current.CancellationToken); Assert.Same(alias, source.Children[0]);
    }

    /// <summary>递归合并保留目标、子节点身份、旧位置与未知字段；未经确认不改树。</summary>
    [Fact]
    public async Task RenameMergeRequiresExactConfirmationAndRetainsNodes()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var source = await state.AddBookmarkFolderAsync(null, "源", token: TestContext.Current.CancellationToken); var target = await state.AddBookmarkFolderAsync(null, "目标", token: TestContext.Current.CancellationToken);
        var sourceChild = await state.AddBookmarkFolderAsync(source, "嵌套", token: TestContext.Current.CancellationToken); var targetChild = await state.AddBookmarkFolderAsync(target, "嵌套", token: TestContext.Current.CancellationToken);
        var kept = new BookmarkNode { Path = "/keep.cbz", Page = "img/007.png", Props = "RightToLeft Future=1", Invalid = true,
            ExtensionData = new() { ["FutureBook"] = JsonSerializer.SerializeToElement(42) } };
        sourceChild.Children!.Add(kept); targetChild.Children!.Add(new() { Path = "/other.cbz" });
        var error = await Assert.ThrowsAsync<BookmarkMergeRequiredException>(() => state.RenameBookmarkAsync(source, "目标", token: TestContext.Current.CancellationToken));
        Assert.Same(target, error.Target); Assert.Contains(source, state.BookmarkRoot.Children!);
        await Assert.ThrowsAsync<BookmarkMergeRequiredException>(() => state.RenameBookmarkAsync(source, "目标", confirmedTarget: sourceChild, token: TestContext.Current.CancellationToken));
        Assert.Same(target, await state.RenameBookmarkAsync(source, "目标", confirmedTarget: target, token: TestContext.Current.CancellationToken));
        Assert.DoesNotContain(source, state.BookmarkRoot.Walk()); Assert.Same(targetChild, target.Children![0]); Assert.Same(kept, targetChild.Children[1]);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(token: TestContext.Current.CancellationToken); var loaded = fresh.BookmarkRoot.Walk().Single(e => e.Path == "/keep.cbz");
        Assert.Equal("img/007.png", loaded.Page); Assert.Equal("RightToLeft Future=1", loaded.Props); Assert.True(loaded.Invalid); Assert.Equal(42, loaded.ExtensionData!["FutureBook"].GetInt32());
    }

    /// <summary>禁止循环、根操作及过期目标；目录移入同名目标采用原自动合并。</summary>
    [Fact]
    public async Task CyclesAndStaleNodesAreRejectedBeforeCommit()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var folder = await state.AddBookmarkFolderAsync(null, "目录", token: TestContext.Current.CancellationToken); var child = await state.AddBookmarkFolderAsync(folder, "子", token: TestContext.Current.CancellationToken);
        Assert.Null(await state.MoveBookmarkAsync(folder, child, token: TestContext.Current.CancellationToken));
        Assert.Null(await state.MoveBookmarkAsync(child, folder, token: TestContext.Current.CancellationToken));
        Assert.Null(await state.MoveBookmarkAsync(child, new() { Path = "/book.cbz" }, token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.MoveBookmarkAsync(folder, folder, 0, token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.MoveBookmarkAsync(child, new() { Children = [] }, token: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.RemoveBookmarkAsync(state.BookmarkRoot, token: TestContext.Current.CancellationToken));
        Assert.Same(child, folder.Children![0]);
        var target = await state.AddBookmarkFolderAsync(null, "目标", token: TestContext.Current.CancellationToken); var same = await state.AddBookmarkFolderAsync(target, "目录", token: TestContext.Current.CancellationToken);
        Assert.Same(same, await state.MoveBookmarkAsync(folder, target, token: TestContext.Current.CancellationToken)); Assert.Same(child, same.Children![0]);
    }

    /// <summary>弹窗确认晚到时原目标若已移走，不能把合并意图静默改成普通重命名。</summary>
    [Fact]
    public async Task StaleMergeConfirmationDoesNotRenameSource()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var source = await state.AddBookmarkFolderAsync(null, "源", TestContext.Current.CancellationToken);
        var target = await state.AddBookmarkFolderAsync(null, "目标", TestContext.Current.CancellationToken);
        var other = await state.AddBookmarkFolderAsync(null, "其他", TestContext.Current.CancellationToken);
        await state.MoveBookmarkAsync(target, other, token: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.RenameBookmarkAsync(source, "目标", TestContext.Current.CancellationToken, target));
        Assert.Equal("源", source.Name); Assert.Same(other, state.Bookmarks.ParentOf(target));
    }

    /// <summary>原登记弹窗分别保留追加、编辑迁移及移除所选父级的行为。</summary>
    [Fact]
    public async Task RegistrationEditUsesOriginalAddEditAndRemoveRules()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var a = await state.AddBookmarkFolderAsync(null, "A", TestContext.Current.CancellationToken);
        var b = await state.AddBookmarkFolderAsync(null, "B", TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var edit = new BookmarkPopupEdit(operation.Book!.CreateMemento(), a, null) { Name = " 原/书 " };
        var node = await state.ApplyBookmarkEditAsync(edit, a, BookmarkPopupResult.Add, TestContext.Current.CancellationToken); Assert.Equal("原_书", node!.Name);
        edit = new(operation.Book.CreateMemento(), a, node) { Name = "改名" };
        Assert.True(edit.IsEdit);
        Assert.Same(node, await state.ApplyBookmarkEditAsync(edit, b, BookmarkPopupResult.Edit, TestContext.Current.CancellationToken)); Assert.Empty(a.Children!); Assert.Same(node, b.Children![0]);
        var duplicate = await state.ApplyBookmarkEditAsync(edit, a, BookmarkPopupResult.Add, TestContext.Current.CancellationToken); Assert.NotSame(node, duplicate);
        await state.ApplyBookmarkEditAsync(edit, a, BookmarkPopupResult.Remove, TestContext.Current.CancellationToken); Assert.Empty(a.Children!); Assert.Single(b.Children);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.Single(fresh.BookmarkRoot.Walk(), e => e.Path == fixture.Images);
    }

    /// <summary>模拟提交半途崩溃，启动按三文件标记恢复同一旧批次及原书签树。</summary>
    [Fact]
    public async Task InterruptedThreeFileCommitRestoresCompletePreviousBatch()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.AddBookmarkFolderAsync(null, "已提交", TestContext.Current.CancellationToken);
        var names = new[] { "UserSetting.json", "History.json", "Bookmark.json" };
        var previous = names.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(fixture.State, name)));
        foreach (var name in names) File.Copy(Path.Combine(fixture.State, name), Path.Combine(fixture.State, name + ".save-backup"));
        await File.WriteAllTextAsync(Path.Combine(fixture.State, ".save-pending.json"), """{"UserSetting.json":true,"History.json":true,"Bookmark.json":true}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), """{"Items":[{"Path":"/uncommitted.cbz"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "Bookmark.json"), """{"Nodes":{"Children":[]}}""", TestContext.Current.CancellationToken);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("已提交", Assert.Single(fresh.BookmarkRoot.Children!).Name);
        foreach (var name in names) Assert.Equal(previous[name], await File.ReadAllTextAsync(Path.Combine(fixture.State, name), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(fixture.State, ".save-pending.json")));
    }

    /// <summary>原 memento 逆序恢复批次顺序，选中父子去重，恢复记录不会持久化。</summary>
    [Fact]
    public async Task RemovalBatchRestoresOriginalIdentityAndOrder()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var a = await state.AddBookmarkFolderAsync(null, "A", token: TestContext.Current.CancellationToken); var b = await state.AddBookmarkFolderAsync(null, "B", token: TestContext.Current.CancellationToken); var c = await state.AddBookmarkFolderAsync(null, "C", token: TestContext.Current.CancellationToken);
        var child = await state.AddBookmarkFolderAsync(a, "子", token: TestContext.Current.CancellationToken);
        await state.RemoveBookmarksAsync([a, child, b], token: TestContext.Current.CancellationToken); Assert.Equal(new[] { c }, state.BookmarkRoot.Children!); Assert.True(state.CanRestoreBookmarks);
        await state.RestoreBookmarksAsync(token: TestContext.Current.CancellationToken); Assert.Equal(new[] { a, b, c }, state.BookmarkRoot.Children!); Assert.Same(child, a.Children![0]); Assert.False(state.CanRestoreBookmarks);
        await state.RemoveBookmarkAsync(c, token: TestContext.Current.CancellationToken); var fresh = new SaveData(fixture.State); await fresh.LoadAsync(token: TestContext.Current.CancellationToken); Assert.False(fresh.CanRestoreBookmarks);
        await state.RestoreBookmarksAsync(token: TestContext.Current.CancellationToken); Assert.Same(c, state.BookmarkRoot.Children![^1]);
    }

    /// <summary>父级已脱离当前树时原恢复不重建父级；超长索引恢复到末尾。</summary>
    [Fact]
    public void RestoreSkipsMissingParentAndClampsIndex()
    {
        var root = new BookmarkNode { Children = [] }; var collection = new BookmarkCollection(root);
        var parent = collection.AddNewFolder(root, "父"); var child = collection.AddNewFolder(parent, "子");
        var removal = collection.Remove(child); collection.Remove(parent); Assert.False(collection.Restore(removal)); Assert.Empty(root.Children);
        root.Children.Add(parent); Assert.True(collection.Restore(removal with { Index = 999 })); Assert.Same(child, parent.Children![^1]);
    }

    /// <summary>递归合并、颜色、删除和恢复失败均回滚引用及权威文件；恢复批次可以重试。</summary>
    [Fact]
    public async Task FailedTreeTransactionsKeepRecoveryAndReferences()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var source = await state.AddBookmarkFolderAsync(null, "源", token: TestContext.Current.CancellationToken); var target = await state.AddBookmarkFolderAsync(null, "目标", token: TestContext.Current.CancellationToken);
        var child = await state.AddBookmarkFolderAsync(source, "子", token: TestContext.Current.CancellationToken); await state.SetBookmarkColorAsync(source, "#FF123456", token: TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(Path.Combine(fixture.State, "Bookmark.json"), cancellationToken: TestContext.Current.CancellationToken);
        var blocked = Path.Combine(fixture.State, "Bookmark.json.tmp"); Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => state.RenameBookmarkAsync(source, "目标", confirmedTarget: target, token: TestContext.Current.CancellationToken));
            Assert.Same(child, source.Children![0]); Assert.Empty(target.Children!); Assert.Contains(source, state.BookmarkRoot.Children!);
            await Assert.ThrowsAnyAsync<Exception>(() => state.SetBookmarkColorAsync(source, "#FF654321", token: TestContext.Current.CancellationToken)); Assert.Equal("#FF123456", source.Color);
            await Assert.ThrowsAnyAsync<Exception>(() => state.RemoveBookmarkAsync(source, token: TestContext.Current.CancellationToken)); Assert.False(state.CanRestoreBookmarks);
            Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(fixture.State, "Bookmark.json"), cancellationToken: TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(blocked); }
        await state.RemoveBookmarkAsync(source, token: TestContext.Current.CancellationToken); Directory.CreateDirectory(blocked);
        try { await Assert.ThrowsAnyAsync<Exception>(() => state.RestoreBookmarksAsync(token: TestContext.Current.CancellationToken)); Assert.True(state.CanRestoreBookmarks); Assert.DoesNotContain(source, state.BookmarkRoot.Walk()); }
        finally { Directory.Delete(blocked); }
        Assert.Same(source, await state.RestoreBookmarksAsync(token: TestContext.Current.CancellationToken)); Assert.Same(child, source.Children![0]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.MoveBookmarkAsync(source, target, token: cancellation.Token)); Assert.Contains(source, state.BookmarkRoot.Children!);
    }

    /// <summary>正式树拖动/取消和登记弹窗进入原业务；书签动作不触发正文刷新。</summary>
    [AvaloniaFact]
    public async Task RealBookmarkTreeDragsAndRegistrationStayIndependentOfReader()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(token: TestContext.Current.CancellationToken);
        var source = await state.AddBookmarkFolderAsync(null, "源", token: TestContext.Current.CancellationToken); var target = await state.AddBookmarkFolderAsync(null, "目标", token: TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var images = new BitmapFactory(new NeeView.Backends.MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.ExecuteAsync("ToggleVisibleBookmarkList"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(window.IsCommandAvailable("RegisterBookmark")); Assert.True(model.ShowBookmarks);
            var refreshes = 0; model.Refreshed += (_, _) => refreshes++;
            var tree = window.FindControl<TreeView>("BookmarkTree")!; tree.SelectedItem = source;
            var register = window.ExecuteAsync("RegisterBookmark");
            for (var i = 0; i < 60 && !window.OwnedWindows.OfType<BookmarkRegistrationWindow>().Any(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            var dialog = Assert.Single(window.OwnedWindows.OfType<BookmarkRegistrationWindow>());
            Assert.Empty(source.Children!);
            dialog.FindControl<Button>("DoneButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await register; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Single(source.Children!); Assert.Same(source.Children![0], model.SelectedBookmark); Assert.Equal(0, refreshes);
            var row = (TreeViewItem)tree.ContainerFromItem(source)!; var targetRow = (TreeViewItem)tree.ContainerFromItem(target)!;
            // 真机回归：树级拖动捕获不能吞掉展开箭头的普通点击。
            var chevron = row.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PART_ExpandCollapseChevron"
                && ReferenceEquals(c.GetVisualAncestors().OfType<TreeViewItem>().First(), row));
            var arrow = chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), window)!.Value;
            window.MouseDown(arrow, MouseButton.Left); window.MouseUp(arrow, MouseButton.Left); Assert.False(row.IsExpanded);
            window.MouseDown(arrow, MouseButton.Left); window.MouseUp(arrow, MouseButton.Left); Assert.True(row.IsExpanded);
            window.UpdateLayout();
            var start = row.TranslatePoint(new Point(90, 12), window)!.Value; var end = targetRow.TranslatePoint(new Point(90, 12), window)!.Value;
            IPointer? pointer = null;
            tree.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (_, e) => pointer = e.Pointer, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); tree.Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.NotNull(pointer); Assert.Null(pointer.Captured); window.MouseUp(end, MouseButton.Left); Assert.Same(state.BookmarkRoot, state.Bookmarks.ParentOf(source));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            row = (TreeViewItem)tree.ContainerFromItem(source)!; targetRow = (TreeViewItem)tree.ContainerFromItem(target)!;
            start = row.TranslatePoint(new Point(90, 12), window)!.Value; end = targetRow.TranslatePoint(new Point(90, 12), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.UpdateLayout();
            // 快速再次按下可触发文件夹双击折叠；使用按下后的实际目标位置，不能沿用旧坐标。
            targetRow = (TreeViewItem)tree.ContainerFromItem(target)!; end = targetRow.TranslatePoint(new Point(90, 12), window)!.Value;
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Same(tree, pointer!.Captured);
            Assert.StartsWith("移入：", window.FindControl<TextBlock>("BookmarkDropHint")!.Text);
            window.MouseUp(end, MouseButton.Left);
            for (var i = 0; i < 60 && state.Bookmarks.ParentOf(source) != target; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Same(target, state.Bookmarks.ParentOf(source)); Assert.Equal(0, operation.Book!.CurrentPage!.Index); Assert.Equal(0, refreshes);
            await window.ApplyBookmarkDropAsync(source, null, false, false); Assert.Same(state.BookmarkRoot, state.Bookmarks.ParentOf(source));
            Assert.Same(source, model.SelectedBookmark); Assert.Single(tree.SelectedItems);
            var blocked = Path.Combine(fixture.State, "Bookmark.json.tmp"); Directory.CreateDirectory(blocked);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => window.ApplyBookmarkDropAsync(source, target, true, false)); Dispatcher.UIThread.RunJobs();
                Assert.Same(source, model.SelectedBookmark); Assert.Same(source, Assert.Single(tree.SelectedItems)); Assert.Same(state.BookmarkRoot, state.Bookmarks.ParentOf(source));
            }
            finally { Directory.Delete(blocked); }
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            // 回归使用当前阶段标签，不能在后续阶段覆盖已提交的第五批截图。
            var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p2-bookmark";
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../acceptance/{phase}-bookmark-layout.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var image = new RenderTargetBitmap(new PixelSize(1200, 800))) { image.Render(window); image.Save(output, PngBitmapEncoderOptions.Default); }
            Assert.True(new FileInfo(output).Length > 1000);
            var count = state.BookmarkRoot.Walk().Count(); var cancel = window.ExecuteAsync("RegisterBookmark");
            for (var i = 0; i < 60 && !window.OwnedWindows.OfType<BookmarkRegistrationWindow>().Any(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Single(window.OwnedWindows.OfType<BookmarkRegistrationWindow>()).FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await cancel; Assert.Equal(count, state.BookmarkRoot.Walk().Count());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    /// <summary>Headless 不冒充 Finder 或废纸篓验证。</summary>
    private sealed class TestPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
