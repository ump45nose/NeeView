using System.Text.Json.Nodes;
using NeeView;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

public sealed class NavigationTests
{
    /// <summary>对真实普通及固实样本乱序重复读取和解码，来源释放后请求流仍可定位。</summary>
    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar.solid.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("7Zip.LZMA.7z")]
    [InlineData("7Zip.solid.7z")]
    public async Task RealArchiveRandomReadAndRelease(string name)
    {
        var token = TestContext.Current.CancellationToken;
        var source = await new ArchiveFactory().OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), token);
        try
        {
            var entries = await source.GetEntriesAsync(token);
            var images = entries.Where(e => ImageFormats.IsImage(e.EntryName)).Reverse().ToArray();
            Assert.NotEmpty(images);
            foreach (var entry in images.Concat(images))
            {
                await using var stream = await source.OpenEntryAsync(entry, token);
                Assert.True(stream.CanSeek);
                Assert.True(stream.Length > 0, $"条目 {entry.Id}: {entry.EntryName} 声明 {entry.Length} 字节，返回空流。");
                using var image = await new MagickImageDecoder().DecodeAsync(stream, new(192, 256), token);
                Assert.Equal(new(192, 256), image.Size);
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.OpenEntryAsync(images[0], cancelled.Token));
            await using var independent = await source.OpenEntryAsync(images[0], token);
            await source.DisposeAsync();
            Assert.True(independent.ReadByte() >= 0);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => source.OpenEntryAsync(images[0], token));
        }
        finally { await source.DisposeAsync(); }
    }

    /// <summary>按原八组顺序核对完整菜单；所有原节点保留，不能按可执行能力过滤。</summary>
    [Fact]
    public void OriginalMenuTreeIsComplete()
    {
        var tree = MenuTree.CreateDefault();
        Assert.Equal(new[] { "文件", "显示", "图像", "跳转", "页面", "书籍", "选项", "帮助" }, tree.Children!.Select(e => e.Name));
        var commands = tree.GetEnumerator().Where(e => e.CommandName is not null).Select(e => e.CommandName).ToArray();
        Assert.Contains("Print", commands); Assert.Contains("OpenScriptsFolder", commands); Assert.Contains("ToggleVisiblePlaylist", commands);
        Assert.Contains("ToggleVisibleFilmStrip", commands); Assert.Contains("ToggleBookmark", commands);
        Assert.True(commands.Length > 100);
    }

    /// <summary>原书签树字段与未知数据往返，历史访问顺序、路径位置及差分解绑保持。</summary>
    [Fact]
    public async Task BookmarkTreeHistoryAndBindingsSurviveReload()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var file = Path.Combine(fixture.State, "Bookmark.json");
        await File.WriteAllTextAsync(file, """{"Format":"NeeView.Bookmark/46.3.0","FutureRoot":7,"Nodes":{"Children":[{"Name":"漫画","Color":"#FF123456","FutureFolder":42,"Children":[{"Name":"旧名称","Path":"/missing.cbz","Page":"img/002.png","Props":"RightToLeft Future=1","Invalid":true,"FutureBook":"keep"}]}]}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var folder = state.BookmarkRoot.Children![0]; var old = folder.Children![0];
        await state.RenameBookmarkAsync(old, "新名称", TestContext.Current.CancellationToken);
        await state.AddBookmarkFolderAsync(folder, "子文件夹", TestContext.Current.CancellationToken);
        await using (var operation = fixture.Operation(state))
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.JumpAsync(2);
            state.SetShortcut("NextPage", ""); state.SetShortcut("PrevPage", "Meta+Right");
            await operation.SaveAsync(); await state.ToggleBookmarkAsync(operation.Book!, TestContext.Current.CancellationToken);
        }
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(fresh.IsBookmark(fixture.Images)); Assert.Equal("003.png", fresh.Find(fixture.Images).Memento!.Page);
        Assert.Equal("", fresh.GetShortcut("NextPage", "Left")); Assert.Equal("Meta+Right", fresh.GetShortcut("PrevPage", "Right"));
        Assert.Equal(fixture.Images, fresh.HistoryEntries[0].Path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!;
        Assert.Equal(7, json["FutureRoot"]!.GetValue<int>()); Assert.Equal(42, json["Nodes"]!["Children"]![0]!["FutureFolder"]!.GetValue<int>());
        Assert.Equal("keep", json["Nodes"]!["Children"]![0]!["Children"]![0]!["FutureBook"]!.GetValue<string>());
        Assert.Equal("新名称", fresh.BookmarkRoot.Children![0].Children![0].Name);
        await fresh.RemoveBookmarkAsync(fresh.BookmarkRoot.Children[0], TestContext.Current.CancellationToken);
        Assert.Single(fresh.BookmarkRoot.Children);
    }
    /// <summary>三文件准备失败不写入权威文件，并恢复编辑前的书签树。</summary>
    [Fact]
    public async Task BookmarkWriteFailureRollsBackTree()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.AddBookmarkFolderAsync(null, "已有目录", TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(Path.Combine(fixture.State, "Bookmark.json"), TestContext.Current.CancellationToken);
        var existing = state.BookmarkRoot.Children![0];
        Directory.CreateDirectory(Path.Combine(fixture.State, "Bookmark.json.tmp"));
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => state.AddBookmarkFolderAsync(null, "不应保存", TestContext.Current.CancellationToken));
            Assert.Single(state.BookmarkRoot.Children!); Assert.Equal("已有目录", state.BookmarkRoot.Children![0].Name);
            Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(fixture.State, "Bookmark.json"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => state.RenameBookmarkAsync(existing, "失败更名", TestContext.Current.CancellationToken));
            Assert.Same(existing, state.BookmarkRoot.Children[0]); Assert.Equal("已有目录", existing.Name);
        }
        finally { Directory.Delete(Path.Combine(fixture.State, "Bookmark.json.tmp")); }
        await state.AddBookmarkFolderAsync(null, "重试成功", TestContext.Current.CancellationToken);
        await state.RenameBookmarkAsync(existing, "重试更名成功", TestContext.Current.CancellationToken);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.Equal(2, fresh.BookmarkRoot.Children!.Count);
    }
    /// <summary>失败保存不能让内存恢复到尚未提交的页面；修复后可正常重试。</summary>
    [Fact]
    public async Task ReadingWriteFailureKeepsCommittedHistory()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SaveAsync();
        var before = state.Find(fixture.Images).Memento!.Page;
        Directory.CreateDirectory(Path.Combine(fixture.State, "Bookmark.json.tmp"));
        try
        {
            await operation.JumpAsync(2);
            await Assert.ThrowsAnyAsync<Exception>(() => operation.SaveAsync());
            Assert.Equal(before, state.Find(fixture.Images).Memento!.Page);
            Assert.Equal(before, state.HistoryEntries[0].Page);
        }
        finally { Directory.Delete(Path.Combine(fixture.State, "Bookmark.json.tmp")); }
        await operation.SaveAsync(); Assert.Equal("003.png", state.Find(fixture.Images).Memento!.Page);
    }
}
